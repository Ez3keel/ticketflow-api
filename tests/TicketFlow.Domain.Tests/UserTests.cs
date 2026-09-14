using TicketFlow.Domain.Entities;
using TicketFlow.Domain.Exceptions;
using Xunit;

namespace TicketFlow.Domain.Tests;

public class UserTests
{
    [Fact]
    public void Email_IsNormalizedToLowerCase()
    {
        var user = new User("Test@Example.com", "hash");

        Assert.Equal("test@example.com", user.Email);
    }

    [Fact]
    public void IssueRefreshToken_AddsActiveToken()
    {
        var user = new User("test@example.com", "hash");
        var now = DateTime.UtcNow;

        var token = user.IssueRefreshToken("token-1", now.AddDays(7));

        Assert.True(token.IsActive(now));
        Assert.Single(user.RefreshTokens);
    }

    [Fact]
    public void RotateRefreshToken_RevokesOldAndIssuesNew()
    {
        var user = new User("test@example.com", "hash");
        var now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now.AddDays(7));

        var newToken = user.RotateRefreshToken("token-1", "token-2", now.AddDays(7), now);

        var oldToken = user.RefreshTokens.First(t => t.Token == "token-1");
        Assert.False(oldToken.IsActive(now));
        Assert.True(newToken.IsActive(now));
        Assert.Equal(2, user.RefreshTokens.Count);
    }

    [Fact]
    public void RotateRefreshToken_WithExpiredToken_Throws()
    {
        var user = new User("test@example.com", "hash");
        var now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now.AddMinutes(-1));

        Assert.Throws<DomainException>(() => user.RotateRefreshToken("token-1", "token-2", now.AddDays(7), now));
    }

    [Fact]
    public void RotateRefreshToken_WithUnknownToken_Throws()
    {
        var user = new User("test@example.com", "hash");
        var now = DateTime.UtcNow;

        Assert.Throws<DomainException>(() => user.RotateRefreshToken("does-not-exist", "token-2", now.AddDays(7), now));
    }

    [Fact]
    public void RotateRefreshToken_ReusingRevokedToken_Throws()
    {
        var user = new User("test@example.com", "hash");
        var now = DateTime.UtcNow;
        user.IssueRefreshToken("token-1", now.AddDays(7));
        user.RotateRefreshToken("token-1", "token-2", now.AddDays(7), now);

        Assert.Throws<DomainException>(() => user.RotateRefreshToken("token-1", "token-3", now.AddDays(7), now));
    }
}
