using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tests.Domain.Infrastructure;
using Xunit;

namespace Tests.Domain;

// ─────────────────────────────────────────────────────────────────────────────
//  Auth flow integration tests
//
//  Covers the login → use → logout lifecycle through the REAL pipeline.
//  No mocking: BCrypt verification, EF Core user lookup, JWT generation,
//  and refresh token issuance all execute exactly as in production.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("IntegrationTests")]
public class AuthFlowTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowTests(SqlServerContainerFixture sqlFixture)
    {
        _factory = new CustomWebApplicationFactory(sqlFixture.ConnectionString);
        _client  = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── Login ─────────────────────────────────────────────────────────────── //

    // 1 — valid credentials → 200 with access token, refresh token, and expiresIn
    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithTokenPair()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@quotes.com", password = "Password123!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("accessToken").GetString().Should()
            .NotBeNullOrEmpty("access token must be present");
        body.GetProperty("refreshToken").GetString().Should()
            .NotBeNullOrEmpty("refresh token must be present");
        body.GetProperty("expiresIn").GetInt32().Should()
            .Be(900, "access tokens are 15-minute (900-second) lifetime");
    }

    // 2 — wrong password → 401
    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@quotes.com", password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 3 — non-existent email → 401 (same response as wrong password — avoids user enumeration)
    [Fact]
    public async Task Login_WithNonExistentEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@nowhere.com", password = "Password123!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 4 — email comparison is case-insensitive (user stored as lowercase on creation)
    [Fact]
    public async Task Login_WithUpperCaseEmail_Returns200()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "ADMIN@QUOTES.COM", password = "Password123!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 5 — access token returned at login must be accepted by a protected endpoint
    [Fact]
    public async Task Login_AccessToken_IsImmediatelyUsableOnProtectedEndpoint()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@quotes.com", password = "Password123!" });
        loginResponse.EnsureSuccessStatusCode();

        var body        = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var accessToken = body.GetProperty("accessToken").GetString()!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);

        var postResponse = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Test", text = "Token is usable immediately." });

        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Logout ────────────────────────────────────────────────────────────── //

    // 6 — logout with a valid refresh token → 204 NoContent
    [Fact]
    public async Task Logout_WithValidRefreshToken_Returns204()
    {
        var login = await LoginAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = login.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // 7 — logout is idempotent: revoking a token that is already revoked → 204
    [Fact]
    public async Task Logout_WithAlreadyRevokedToken_Returns204()
    {
        var login = await LoginAsync();

        // First logout
        await _client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = login.RefreshToken });

        // Second logout with the same token — should not throw
        var response = await _client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = login.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // 8 — after logout, the refresh token is unusable for rotation
    [Fact]
    public async Task Logout_ThenRefresh_Returns401()
    {
        var login = await LoginAsync();

        await _client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = login.RefreshToken });

        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ──────────────────────────────────────────────────────────── //

    private async Task<(string AccessToken, string RefreshToken)> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@quotes.com", password = "Password123!" });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return (
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!
        );
    }
}
