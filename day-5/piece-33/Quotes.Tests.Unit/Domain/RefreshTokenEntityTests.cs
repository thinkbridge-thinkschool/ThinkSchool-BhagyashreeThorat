using FluentAssertions;
using QuotesApi.Entities;
using Xunit;

namespace Quotes.Tests.Unit.Domain;

public class RefreshTokenEntityTests
{
    // ──────────────────────────────────────────────────────────────────
    //  IsExpired — relies on DateTimeOffset.UtcNow inside the entity,
    //  so we control ExpiresAt rather than the clock.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IsExpired_ExpiresInFuture_ReturnsFalse()
    {
        // Arrange
        var token = RefreshToken.Create(
            tokenHash:  "abc",
            userId:     1,
            expiresAt:  DateTimeOffset.UtcNow.AddDays(7),
            familyId:   Guid.NewGuid(),
            createdAt:  DateTimeOffset.UtcNow);

        // Act & Assert
        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ExpiredInPast_ReturnsTrue()
    {
        // Arrange
        var token = RefreshToken.Create(
            tokenHash:  "abc",
            userId:     1,
            expiresAt:  DateTimeOffset.UtcNow.AddDays(-1),
            familyId:   Guid.NewGuid(),
            createdAt:  DateTimeOffset.UtcNow.AddDays(-8));

        // Act & Assert
        token.IsExpired.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    //  IsRevoked
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRevoked_FreshToken_ReturnsFalse()
    {
        // Arrange
        var token = RefreshToken.Create("abc", 1,
            DateTimeOffset.UtcNow.AddDays(7), Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Act & Assert
        token.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public void IsRevoked_AfterRevoke_ReturnsTrue()
    {
        // Arrange
        var token = RefreshToken.Create("abc", 1,
            DateTimeOffset.UtcNow.AddDays(7), Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Act
        token.Revoke(DateTimeOffset.UtcNow);

        // Assert
        token.IsRevoked.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Revoke
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Revoke_SetsRevokedAtToProvidedTimestamp()
    {
        // Arrange
        var token = RefreshToken.Create("hash", 1,
            DateTimeOffset.UtcNow.AddDays(7), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var revokedAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        token.Revoke(revokedAt);

        // Assert
        token.RevokedAt.Should().Be(revokedAt);
    }

    [Fact]
    public void Revoke_WithReplacedByHash_SetsReplacedByTokenHash()
    {
        // Arrange
        var token = RefreshToken.Create("hash", 1,
            DateTimeOffset.UtcNow.AddDays(7), Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Act
        token.Revoke(DateTimeOffset.UtcNow, replacedByTokenHash: "new_hash_value");

        // Assert
        token.ReplacedByTokenHash.Should().Be("new_hash_value");
    }

    [Fact]
    public void Revoke_WithoutReplacedByHash_ReplacedByTokenHashIsNull()
    {
        // Arrange
        var token = RefreshToken.Create("hash", 1,
            DateTimeOffset.UtcNow.AddDays(7), Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Act — logout path: no replacement token
        token.Revoke(DateTimeOffset.UtcNow);

        // Assert
        token.ReplacedByTokenHash.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Create factory
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllPropertiesCorrectly()
    {
        // Arrange
        const string hash     = "testhash";
        const int    userId   = 5;
        var          familyId = Guid.NewGuid();
        var          expires  = DateTimeOffset.UtcNow.AddDays(7);
        var          created  = DateTimeOffset.UtcNow;

        // Act
        var token = RefreshToken.Create(hash, userId, expires, familyId, created);

        // Assert
        token.TokenHash.Should().Be(hash);
        token.UserId.Should().Be(userId);
        token.ExpiresAt.Should().Be(expires);
        token.FamilyId.Should().Be(familyId);
        token.CreatedAt.Should().Be(created);
        token.RevokedAt.Should().BeNull();
        token.ReplacedByTokenHash.Should().BeNull();
    }
}
