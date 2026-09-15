using Microsoft.AspNetCore.SignalR;

namespace TicketFlow.Infrastructure.Realtime;

// Only ever has live client connections inside the Api process (that's where
// MapHub<TicketFlowHub>() runs). The Worker also references this class purely as a
// type parameter for IHubContext<TicketFlowHub> -- it publishes through the Redis
// backplane without ever hosting a connection itself.
public class TicketFlowHub : Hub
{
    public Task JoinSession(Guid sessionId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(sessionId));

    public Task LeaveSession(Guid sessionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(sessionId));

    public static string GroupName(Guid sessionId) => $"session:{sessionId}";
}
