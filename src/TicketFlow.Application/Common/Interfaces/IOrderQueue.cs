namespace TicketFlow.Application.Common.Interfaces;

public interface IOrderQueue
{
    Task EnqueueConfirmationAsync(Guid orderId, CancellationToken cancellationToken = default);
}
