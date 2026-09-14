namespace TicketFlow.Application.Auth.Dtos;

public record AuthResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc);
