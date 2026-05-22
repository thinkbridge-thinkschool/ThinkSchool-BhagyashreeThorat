using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Services;
using Tests.Domain.Infrastructure;
using Xunit;

namespace Tests.Domain;

// ─────────────────────────────────────────────────────────────────────────────
//  Auth integration tests — refresh token lifecycle
//
//  Each test method gets its own factory instance (xUnit creates a new
//  test-class instance per method) which means a completely fresh
//  SQLite in-memory database and independent admin user seeding.
//  No test can ever pollute another.
// ─────────────────────────────────────────────────────────────────────────────

public class AuthIntegrationTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests()
    {
        _factory = new CustomWebApplicationFactory();
        _client  = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── 1. Happy path: valid token returns a fresh token pair ───────────── //

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokenPair()
    {
        var login = await LoginAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadAsync<LoginResponse>(response);
        body.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBe(login.RefreshToken, "rotation must produce a distinct token");
        body.ExpiresIn.Should().Be(900);
    }

    // ── 2. Expired token must be rejected ───────────────────────────────── //

    [Fact]
    public async Task Refresh_WithExpiredToken_Returns401()
    {
        var login = await LoginAsync();

        await DbSeeder.ExpireRefreshTokenAsync(_factory.Services, login.RefreshToken);

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── 3. Revoked token (via logout) must be rejected ──────────────────── //

    [Fact]
    public async Task Refresh_WithRevokedToken_Returns401()
    {
        var login = await LoginAsync();

        await _client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = login.RefreshToken });

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── 4. Rotation produces a distinct, immediately usable token ────────── //

    [Fact]
    public async Task Refresh_RotatesToken_NewTokenIsDistinctAndUsable()
    {
        var login = await LoginAsync();

        // A → B
        var firstRefresh = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenB = (await ReadAsync<LoginResponse>(firstRefresh)).RefreshToken;

        tokenB.Should().NotBe(login.RefreshToken, "rotation must produce a new token");

        // B → C  (B is still valid)
        var secondRefresh = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokenB });
        secondRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── 5. Replay attack triggers full family revocation ─────────────────── //
    //
    //   login  → Token A
    //   refresh(A)  → Token B    (A is now rotated/revoked)
    //   refresh(A)  → 401        (reuse detected, entire family revoked)
    //   refresh(B)  → 401        (B was in the same family — now dead)

    [Fact]
    public async Task Refresh_ReuseDetected_RevokesEntireFamily_TokenBBecomesUnusable()
    {
        var login  = await LoginAsync();
        var tokenA = login.RefreshToken;

        // Legitimate rotation: A → B
        var rotateResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokenA });
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenB = (await ReadAsync<LoginResponse>(rotateResponse)).RefreshToken;

        // Replay token A — reuse detected
        var replayResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokenA });
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Token B must also be dead (same family was revoked)
        var tokenBResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokenB });
        tokenBResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify DB: both tokens carry a RevokedAt and share the same FamilyId
        using var scope = _factory.Services.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hashA = RefreshTokenService.HashToken(tokenA);
        var hashB = RefreshTokenService.HashToken(tokenB);

        var dbTokenA = await db.RefreshTokens.FirstAsync(t => t.TokenHash == hashA);
        var dbTokenB = await db.RefreshTokens.FirstAsync(t => t.TokenHash == hashB);

        dbTokenA.IsRevoked.Should().BeTrue("Token A should be revoked after replay detection");
        dbTokenB.IsRevoked.Should().BeTrue("Token B should be revoked because its family was revoked");
        dbTokenA.FamilyId.Should().Be(dbTokenB.FamilyId, "both tokens belong to the same rotation family");
    }

    // ── Helpers ──────────────────────────────────────────────────────────── //

    private async Task<LoginResponse> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@quotes.com", password = "Password123!" });
        response.EnsureSuccessStatusCode();
        return await ReadAsync<LoginResponse>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOpts);
        result.Should().NotBeNull();
        return result!;
    }
}
