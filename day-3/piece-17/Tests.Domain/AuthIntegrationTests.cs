using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Services;
using Xunit;

namespace Tests.Domain;

// ------------------------------------------------------------------ //
//  Test factory — isolated in-memory SQLite per fixture instance
// ------------------------------------------------------------------ //

public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(opts => opts.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

// ------------------------------------------------------------------ //
//  Integration tests
// ------------------------------------------------------------------ //

public class AuthIntegrationTests : IClassFixture<AuthTestFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly AuthTestFactory _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests(AuthTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // 1 — happy path: valid token returns a fresh token pair
    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokenPair()
    {
        var login = await LoginAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadAsync<LoginResponse>(response);
        Assert.False(string.IsNullOrEmpty(body.AccessToken));
        Assert.False(string.IsNullOrEmpty(body.RefreshToken));
        Assert.NotEqual(login.RefreshToken, body.RefreshToken); // rotated
        Assert.Equal(900, body.ExpiresIn);
    }

    // 2 — expired token must be rejected
    [Fact]
    public async Task Refresh_WithExpiredToken_Returns401()
    {
        var login = await LoginAsync();
        var hash = RefreshTokenService.HashToken(login.RefreshToken);

        // Directly back-date the token in the database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var past = DateTimeOffset.UtcNow.AddDays(-1);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE RefreshTokens SET ExpiresAt = {past} WHERE TokenHash = {hash}");
        }

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 3 — revoked token (via logout) must be rejected
    [Fact]
    public async Task Refresh_WithRevokedToken_Returns401()
    {
        var login = await LoginAsync();

        await _client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = login.RefreshToken });

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 4 — each refresh produces a distinct token; the new token is immediately usable
    //   (reusing the old token after rotation is covered by test 5 — that triggers family
    //    revocation, so we do NOT replay A here or token B would become dead too)
    [Fact]
    public async Task Refresh_RotatesToken_NewTokenIsDistinctAndUsable()
    {
        var login = await LoginAsync();

        // Token A → Token B
        var firstRefresh = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var tokenB = (await ReadAsync<LoginResponse>(firstRefresh)).RefreshToken;

        Assert.NotEqual(login.RefreshToken, tokenB); // rotation produced a new token

        // Token B → Token C  (B is still alive and rotatable)
        var secondRefresh = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokenB });
        Assert.Equal(HttpStatusCode.OK, secondRefresh.StatusCode);
    }

    // 5 — MOST IMPORTANT: replay attack triggers full family revocation
    //
    //   login  →  Token A
    //   refresh(A)  →  Token B  (A is now rotated/revoked)
    //   refresh(A)  →  401  (reuse detected, entire family revoked)
    //   refresh(B)  →  401  (B was in the same family — now revoked too)
    [Fact]
    public async Task Refresh_ReuseDetected_RevokesEntireFamily_TokenBBecomesUnusable()
    {
        var login = await LoginAsync();
        var tokenA = login.RefreshToken;

        // First legitimate rotation: A → B
        var rotateResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokenA });
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        var tokenB = (await ReadAsync<LoginResponse>(rotateResponse)).RefreshToken;

        // Replay old token A — reuse detected
        var replayResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokenA });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        // Token B must also be dead because the whole family was revoked
        var tokenBResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokenB });
        Assert.Equal(HttpStatusCode.Unauthorized, tokenBResponse.StatusCode);

        // Verify DB state: both tokens have RevokedAt set
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hashA = RefreshTokenService.HashToken(tokenA);
        var hashB = RefreshTokenService.HashToken(tokenB);

        var dbTokenA = await db.RefreshTokens.FirstAsync(t => t.TokenHash == hashA);
        var dbTokenB = await db.RefreshTokens.FirstAsync(t => t.TokenHash == hashB);

        Assert.True(dbTokenA.IsRevoked, "Token A should be revoked after replay.");
        Assert.True(dbTokenB.IsRevoked, "Token B should be revoked after family revocation.");
        Assert.Equal(dbTokenA.FamilyId, dbTokenB.FamilyId); // same family
    }

    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //

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
        Assert.NotNull(result);
        return result!;
    }
}
