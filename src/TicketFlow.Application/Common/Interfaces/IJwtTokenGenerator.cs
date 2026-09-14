using TicketFlow.Domain.Entities;

namespace TicketFlow.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(User user);

    string GenerateRefreshToken();
}
