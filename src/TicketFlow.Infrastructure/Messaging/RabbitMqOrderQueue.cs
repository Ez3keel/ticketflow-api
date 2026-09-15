using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TicketFlow.Application.Common.Interfaces;

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

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: RabbitMqSettings.OrderConfirmationQueue,
            mandatory: false,
            basicProperties: new BasicProperties { Persistent = true },
            body: body,
            cancellationToken: cancellationToken);
    }
}
