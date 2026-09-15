using Microsoft.AspNetCore.SignalR;
using TicketFlow.Application.Common.Interfaces;

namespace TicketFlow.Infrastructure.Realtime;

public class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<TicketFlowHub> _hub;

    public SignalRRealtimeNotifier(IHubContext<TicketFlowHub> hub)
    {
        _hub = hub;
    }

    public Task NotifySeatStatusChangedAsync(Guid sessionId, Guid seatId, string status, CancellationToken cancellationToken = default)
        => _hub.Clients.Group(TicketFlowHub.GroupName(sessionId))
            .SendAsync("SeatStatusChanged", new { seatId, status }, cancellationToken);

    public Task NotifyOrderConfirmedAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default)
        => _hub.Clients.User(userId.ToString())
            .SendAsync("OrderConfirmed", new { orderId }, cancellationToken);
}
