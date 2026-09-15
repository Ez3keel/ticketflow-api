using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;

namespace TicketFlow.Infrastructure.Realtime;

// SignalR's default IUserIdProvider reads ClaimTypes.NameIdentifier, but our JWT
// setup disables inbound claim remapping (see JwtTokenGenerator/Program.cs), so the
// claim is literally "sub". Without this, Clients.User(userId) would never match
// any connection.
public class JwtSubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
