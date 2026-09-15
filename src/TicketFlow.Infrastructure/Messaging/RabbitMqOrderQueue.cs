using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TicketFlow.Application.Common.Interfaces;

namespace TicketFlow.Infrastructure.Messaging;

public class RabbitMqOrderQueue : IOrderQueue
{
    private readonly IConnection _connection;

    public RabbitMqOrderQueue(IConnection connection)
    {
        _connection = connection;
    }

    public async Task EnqueueConfirmationAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

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
