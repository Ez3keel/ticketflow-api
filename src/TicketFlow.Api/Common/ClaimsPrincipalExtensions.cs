using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TicketFlow.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Token is missing the 'sub' claim.");

        return Guid.Parse(value);
    }
}
