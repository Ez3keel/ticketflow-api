using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using TicketFlow.Application.Common.Interfaces;
using TicketFlow.Application.Observability;

namespace TicketFlow.Infrastructure.Messaging;

public class RabbitMqOrderQueue : IOrderQueue
{
    private readonly Lazy<IConnection> _connection;

    // Depends on Lazy<IConnection>, not IConnection directly: ASP.NET Core resolves
    // a whole constructor dependency graph as soon as anything needs an instance --
    // ReservationService takes IOrderQueue, so simply handling a Reserve or Cancel
    // request (which never touch the queue) would otherwise force a live RabbitMQ
    // connection just because RabbitMqOrderQueue happened to be in the graph. With
    // Lazy<T>, that connection attempt is deferred until EnqueueConfirmationAsync
    // actually runs (RequestConfirmationAsync only). Found via the Fase 7
    // integration tests: reserving a seat failed with a RabbitMQ auth error that had
    // nothing to do with reservations.
    public RabbitMqOrderQueue(Lazy<IConnection> connection)
    {
        _connection = connection;
    }

    public async Task EnqueueConfirmationAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var channel = await _connection.Value.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: RabbitMqSettings.OrderConfirmationQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new OrderConfirmationMessage(orderId)));

        // A queue message is not an HTTP request, so nothing propagates trace context
        // across it automatically the way ASP.NET Core's own instrumentation does for
        // an outgoing HttpClient call. Doing it by hand -- inject here, extract in
        // OrderConfirmationConsumer -- is what lets a single trace in Jaeger show the
        // Api's publish span and the Worker's processing span as one connected chain,
        // instead of two unrelated traces that happen to reference the same order id.
        using var activity = TicketFlowActivitySource.Instance.StartActivity(
            $"{RabbitMqSettings.OrderConfirmationQueue} publish", ActivityKind.Producer);

        var headers = new Dictionary<string, object?>();
        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(activity?.Context ?? Activity.Current?.Context ?? default, Baggage.Current),
            headers,
            static (carrier, key, value) => carrier[key] = value);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: RabbitMqSettings.OrderConfirmationQueue,
            mandatory: false,
            basicProperties: new BasicProperties { Persistent = true, Headers = headers },
            body: body,
            cancellationToken: cancellationToken);
    }
}
