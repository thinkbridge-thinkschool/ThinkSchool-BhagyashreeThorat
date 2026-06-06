using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quotes.Business.Services;
using Quotes.Repository.Context;
using Quotes.Repository.Entities;
using Quotes.Model.Shared;
using Quotes.Tests.Unit.Fakes;
using Xunit;

namespace Quotes.Tests.Unit.Services;

/// <summary>
/// Tests for RefreshTokenService business logic.
/// Uses EF Core InMemory provider to keep tests fast and isolated — no file I/O,
/// no network, no shared state between tests (each gets its own database name).
/// </summary>
public class RefreshTokenServiceTests : IDisposable
{
    private readonly AppDbContext           _db;
    private readonly ITokenService          _tokenService;
    private readonly FakeClock              _clock;
    private readonly ILogger<RefreshTokenService> _logger;
    private readonly RefreshTokenService    _sut;

    public RefreshTokenServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // unique DB per test
            .Options;

        _db           = new AppDbContext(options);
        _tokenService = Substitute.For<ITokenService>();
        _clock        = new FakeClock();
        _logger       = Substitute.For<ILogger<RefreshTokenService>>();

        // RefreshTokenService now requires IOptions<JwtOptions>. Only
        // RefreshTokenLifetimeDays is read by the service — pin it to 7 so the
        // "expires in seven days" test asserts against an explicit value.
        var jwtOptions = Options.Create(new JwtOptions { RefreshTokenLifetimeDays = 7 });

        _sut          = new RefreshTokenService(_db, _tokenService, _clock, _logger, jwtOptions);
    }

    public void Dispose() => _db.Dispose();

    // Seed a persisted user so FK constraints are satisfied when issuing tokens.
    private async Task<User> SeedUserAsync()
    {
        var user = User.Create("test@example.com", "hashed");
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user; // Id is set by EF after SaveChanges
    }

    // ──────────────────────────────────────────────────────────────────
    //  IssueAsync
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IssueAsync_ValidUserId_StoresHashedTokenInDatabase()
    {
        // Arrange
        var user = await SeedUserAsync();
        _tokenService.GenerateRefreshToken().Returns("raw_token");

        // Act
        var returned = await _sut.IssueAsync(user.Id);

        // Assert — caller receives the raw token; only the hash is persisted
        returned.Should().Be("raw_token");

        var expectedHash = RefreshTokenService.HashToken("raw_token");
        var stored = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == expectedHash);
        stored.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task IssueAsync_NewToken_ExpiresInSevenDays()
    {
        // Arrange — pin the clock to a known instant
        var user    = await SeedUserAsync();
        var fixedNow = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _clock.UtcNow = fixedNow;
        _tokenService.GenerateRefreshToken().Returns("token");

        // Act
        await _sut.IssueAsync(user.Id);

        // Assert
        var stored = await _db.RefreshTokens.SingleAsync();
        stored.ExpiresAt.Should().Be(fixedNow.AddDays(7));
    }

    // ──────────────────────────────────────────────────────────────────
    //  HashToken (static utility, internal)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HashToken_SameInput_AlwaysReturnsSameHash()
    {
        // Arrange & Act
        var hash1 = RefreshTokenService.HashToken("deterministic");
        var hash2 = RefreshTokenService.HashToken("deterministic");

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashToken_ReturnsLowercase64CharHexString()
    {
        // Arrange & Act
        var hash = RefreshTokenService.HashToken("any_token");

        // Assert
        hash.Should().HaveLength(64, "SHA-256 produces 32 bytes = 64 hex chars");
        hash.Should().MatchRegex("^[0-9a-f]+$", "hex string must be lowercase");
    }

    // ──────────────────────────────────────────────────────────────────
    //  RotateAsync — happy path
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RotateAsync_ValidToken_ReturnsNewRawTokenAndUser()
    {
        // Arrange
        var user = await SeedUserAsync();
        _tokenService.GenerateRefreshToken().Returns("token_a", "token_b");
        await _sut.IssueAsync(user.Id);

        // Act
        var (newToken, returnedUser) = await _sut.RotateAsync("token_a");

        // Assert
        newToken.Should().Be("token_b");
        returnedUser.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task RotateAsync_ValidToken_RevokesOldTokenWithReplacement()
    {
        // Arrange
        var user = await SeedUserAsync();
        _tokenService.GenerateRefreshToken().Returns("token_a", "token_b");
        await _sut.IssueAsync(user.Id);

        // Act
        await _sut.RotateAsync("token_a");

        // Assert — old token is revoked and records the successor hash
        var hashA = RefreshTokenService.HashToken("token_a");
        var oldToken = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == hashA);
        oldToken.IsRevoked.Should().BeTrue();
        oldToken.ReplacedByTokenHash.Should().NotBeNull(
            "rotation records the successor so replay detection can fire later");
    }

    [Fact]
    public async Task RotateAsync_ValidToken_NewTokenInheritsSameFamilyId()
    {
        // Arrange
        var user = await SeedUserAsync();
        _tokenService.GenerateRefreshToken().Returns("token_a", "token_b");
        await _sut.IssueAsync(user.Id);

        // Act
        await _sut.RotateAsync("token_a");

        // Assert — both tokens in the chain share one FamilyId
        var tokens = await _db.RefreshTokens.ToListAsync();
        tokens.Select(t => t.FamilyId).Distinct().Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────────────────────────
    //  RotateAsync — failure paths
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RotateAsync_ExpiredToken_ThrowsInvalidOperationException()
    {
        // Arrange — issue token 8 days in the past so it is already expired
        var user = await SeedUserAsync();
        _clock.UtcNow = DateTimeOffset.UtcNow.AddDays(-8); // issued 8 days ago → expires 1 day ago
        _tokenService.GenerateRefreshToken().Returns("expired");
        await _sut.IssueAsync(user.Id);

        _clock.UtcNow = DateTimeOffset.UtcNow; // advance to "now"

        // Act
        Func<Task> act = () => _sut.RotateAsync("expired");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task RotateAsync_LoggedOutRevokedToken_ThrowsInvalidOperationException()
    {
        // Arrange — revoke via logout (no ReplacedByTokenHash set)
        var user = await SeedUserAsync();
        _tokenService.GenerateRefreshToken().Returns("token");
        await _sut.IssueAsync(user.Id);
        await _sut.RevokeAsync("token"); // simulate logout

        // Act
        Func<Task> act = () => _sut.RotateAsync("token");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*revoked*");
    }

    [Fact]
    public async Task RotateAsync_UnknownToken_ThrowsInvalidOperationException()
    {
        // Arrange & Act
        Func<Task> act = () => _sut.RotateAsync("completely_unknown_token");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid*");
    }

    // ──────────────────────────────────────────────────────────────────
    //  RotateAsync — replay / reuse attack (family revocation)
    //
    //  Sequence:
    //    1. issue(user) → token_a
    //    2. rotate(token_a) → token_b   (token_a revoked, ReplacedByTokenHash = hash_b)
    //    3. rotate(token_a) again       ← attacker replays old token
    //       → family revoked (token_b dies too), throws 401
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RotateAsync_ReplayAttack_RevokesFamilyAndThrows()
    {
        // Arrange
        var user = await SeedUserAsync();
        _tokenService.GenerateRefreshToken().Returns("token_a", "token_b");

        await _sut.IssueAsync(user.Id);                  // issues token_a
        await _sut.RotateAsync("token_a");               // rotates → token_b; marks token_a as replaced

        // Act — attacker re-submits the already-rotated token_a
        Func<Task> act = () => _sut.RotateAsync("token_a");

        // Assert — service throws immediately
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*revoked*");

        // Token_b must also be dead — entire family was nuked
        var hashB   = RefreshTokenService.HashToken("token_b");
        var tokenB  = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == hashB);
        tokenB.IsRevoked.Should().BeTrue(
            "replay detection must revoke every token in the same family");
    }

    // ──────────────────────────────────────────────────────────────────
    //  RevokeAsync
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAsync_ValidToken_RevokesWithoutSettingReplacement()
    {
        // Arrange
        var user = await SeedUserAsync();
        _tokenService.GenerateRefreshToken().Returns("token");
        await _sut.IssueAsync(user.Id);

        // Act
        await _sut.RevokeAsync("token");

        // Assert
        var hash  = RefreshTokenService.HashToken("token");
        var token = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == hash);
        token.IsRevoked.Should().BeTrue();
        token.ReplacedByTokenHash.Should().BeNull(
            "logout does not set a successor — that would falsely trigger replay detection");
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_IsIdempotent()
    {
        // Arrange
        var user = await SeedUserAsync();
        _tokenService.GenerateRefreshToken().Returns("token");
        await _sut.IssueAsync(user.Id);
        await _sut.RevokeAsync("token"); // first revoke

        // Act — second revoke must not throw
        Func<Task> act = () => _sut.RevokeAsync("token");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_DoesNothing()
    {
        // Arrange & Act
        Func<Task> act = () => _sut.RevokeAsync("unknown_token");

        // Assert
        await act.Should().NotThrowAsync();
    }
}
