namespace TicketFlow.Application.Common.Interfaces;

public interface IRealtimeNotifier
{
    Task NotifySeatStatusChangedAsync(Guid sessionId, Guid seatId, string status, CancellationToken cancellationToken = default);

    Task NotifyOrderConfirmedAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default);
}
