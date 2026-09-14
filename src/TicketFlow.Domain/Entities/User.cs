using TicketFlow.Domain.Common;
using TicketFlow.Domain.Exceptions;

namespace TicketFlow.Domain.Entities;

public class User : Entity
{
    private readonly List<RefreshToken> _refreshTokens = new();

    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private User()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    public User(string email, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Password hash is required.");

        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
    }

    public RefreshToken IssueRefreshToken(string token, DateTime expiresAtUtc)
    {
        var refreshToken = new RefreshToken(Id, token, expiresAtUtc);
        _refreshTokens.Add(refreshToken);
        return refreshToken;
    }

    public RefreshToken RotateRefreshToken(string currentToken, string newToken, DateTime newExpiresAtUtc, DateTime utcNow)
    {
        var existing = _refreshTokens.FirstOrDefault(t => t.Token == currentToken)
            ?? throw new DomainException("Refresh token not found.");

        if (!existing.IsActive(utcNow))
            throw new DomainException("Refresh token is no longer active.");

        existing.Revoke(utcNow);
        return IssueRefreshToken(newToken, newExpiresAtUtc);
    }
}
