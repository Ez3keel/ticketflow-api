using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TicketFlow.Application.Observability;
using TicketFlow.Application.Reservations;
using TicketFlow.Infrastructure.Messaging;

namespace TicketFlow.Worker;

public class OrderConfirmationConsumer : BackgroundService
{
    // Stands in for a real payment gateway call, so the async nature of the
    // pipeline is actually observable: a client polling GET /orders/{id} right
    // after confirming sees Processing for a couple of seconds before Confirmed.
    private static readonly TimeSpan SimulatedPaymentProcessingTime = TimeSpan.FromSeconds(2);

    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderConfirmationConsumer> _logger;
    private IChannel? _channel;

    public OrderConfirmationConsumer(IConnection connection, IServiceScopeFactory scopeFactory, ILogger<OrderConfirmationConsumer> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: RabbitMqSettings.OrderConfirmationQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // One in-flight message per worker process at a time: simple, predictable
        // behavior for a portfolio demo. Scaling throughput would mean running more
        // worker instances (or raising this), not adding threads inside one process.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: RabbitMqSettings.OrderConfirmationQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Listening for order confirmations on '{Queue}'.", RabbitMqSettings.OrderConfirmationQueue);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel!;

        // The other half of the manual propagation in RabbitMqOrderQueue: extract the
        // trace context the Api injected into the message headers and start this
        // span as its child, so Jaeger renders "reserve -> ... -> publish -> process
        // -> confirm" as one trace instead of two disconnected ones that just happen
        // to share an order id.
        var parentContext = Propagators.DefaultTextMapPropagator.Extract(default, ea.BasicProperties.Headers, ExtractHeaderValue);
        Baggage.Current = parentContext.Baggage;

        using var activity = TicketFlowActivitySource.Instance.StartActivity(
            $"{RabbitMqSettings.OrderConfirmationQueue} process", ActivityKind.Consumer, parentContext.ActivityContext);

        try
        {
            var message = JsonSerializer.Deserialize<OrderConfirmationMessage>(Encoding.UTF8.GetString(ea.Body.Span))
                ?? throw new InvalidOperationException("Received an empty order confirmation message.");

            activity?.SetTag("ticketflow.order_id", message.OrderId);

            _logger.LogInformation("Processing confirmation for order {OrderId}.", message.OrderId);
            await Task.Delay(SimulatedPaymentProcessingTime);

            using var scope = _scopeFactory.CreateScope();
            var processingService = scope.ServiceProvider.GetRequiredService<OrderProcessingService>();
            await processingService.ConfirmOrderAsync(message.OrderId);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            _logger.LogInformation("Order {OrderId} confirmed.", message.OrderId);
        }
        catch (Exception ex)
        {
            // No dead-letter exchange configured here -- a real deployment would
            // route rejected messages there instead of dropping them, so a human
            // can inspect what went wrong instead of losing the order silently.
            _logger.LogError(ex, "Failed to process order confirmation message; dropping it.");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
            await _channel.CloseAsync(cancellationToken);

        await base.StopAsync(cancellationToken);
    }

    // RabbitMQ.Client round-trips header values as byte[] on the consuming side even
    // though RabbitMqOrderQueue set them as plain strings when publishing.
    private static IEnumerable<string> ExtractHeaderValue(IDictionary<string, object?>? headers, string key)
    {
        if (headers is not null && headers.TryGetValue(key, out var value) && value is byte[] bytes)
            return [Encoding.UTF8.GetString(bytes)];

        return [];
    }
}
