using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Services;
using Tests.Domain.Infrastructure;
using Xunit;

namespace Tests.Domain;

// ─────────────────────────────────────────────────────────────────────────────
//  FakeClock / IClock integration tests
//
//  The production code uses IClock (injected) to SET timestamps:
//    • RefreshToken.ExpiresAt  = clock.UtcNow + 7 days   (in RefreshTokenService)
//    • CollectionItem.AddedAt  = clock.UtcNow             (in the collection endpoint)
//
//  These tests set the FakeClock to a precise, known time, call real API
//  endpoints, then assert that stored timestamps reflect the fake time —
//  proving that the DI wiring is correct and no code has a hidden
//  DateTimeOffset.UtcNow call that bypasses the abstraction.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("IntegrationTests")]
public class ClockBehaviorTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClockBehaviorTests(SqlServerContainerFixture sqlFixture)
    {
        _factory = new CustomWebApplicationFactory(sqlFixture.ConnectionString);
        _client  = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // 1 — refresh token ExpiresAt is set relative to FakeClock, not wall-clock
    //
    //     Set the clock to a fixed past date.  Login (which calls IssueAsync).
    //     Verify the DB record's ExpiresAt equals fakeNow + 7 days.
    //     If ServiceExtensions hard-coded DateTimeOffset.UtcNow instead of
    //     IClock, this test would fail because ExpiresAt would be ~today not ~2020.
    [Fact]
    public async Task Login_RefreshTokenExpiresAt_ReflectsFakeClockNotWallClock()
    {
        var fakeNow = new DateTimeOffset(2020, 6, 1, 12, 0, 0, TimeSpan.Zero);
        _factory.Clock.SetTo(fakeNow);

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@quotes.com", password = "Password123!" });
        response.EnsureSuccessStatusCode();

        var body         = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var refreshToken = body.GetProperty("refreshToken").GetString()!;
        var tokenHash    = RefreshTokenService.HashToken(refreshToken);

        using var scope = _factory.Services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbToken     = await db.RefreshTokens.FirstAsync(t => t.TokenHash == tokenHash);

        var expectedExpiry = fakeNow.AddDays(7);
        dbToken.ExpiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(5),
            "ExpiresAt must be based on IClock.UtcNow, not real wall-clock time");
    }

    // 2 — advancing the clock changes the ExpiresAt of subsequently issued tokens
    //
    //     Token A issued at fakeNow=T1.  Rotate it.  Token B issued at T1.
    //     Advance clock to T2.  Login again.  New token issued at T2.
    //     Token B's ExpiresAt ≈ T1+7d, new token's ExpiresAt ≈ T2+7d.
    //
    //     IMPORTANT: T1 must be the real current time (not a date in the past)
    //     because RefreshToken.IsExpired uses DateTimeOffset.UtcNow directly.
    //     If T1 is in the past, issued tokens are immediately expired and rotation fails.
    //     IClock only controls timestamp SETTING, not the IsExpired READ check.
    [Fact]
    public async Task RefreshTokenRotation_ExpiresAt_TracksAdvancedClock()
    {
        var t1 = DateTimeOffset.UtcNow;
        _factory.Clock.SetTo(t1);

        var firstLogin = await LoginAsync();

        // Rotate the first token (issued at T1)
        var rotateResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = firstLogin.RefreshToken });
        rotateResponse.EnsureSuccessStatusCode();
        var tokenB = (await rotateResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts))
            .GetProperty("refreshToken").GetString()!;

        // Advance clock by 30 days
        var t2 = t1.AddDays(30);
        _factory.Clock.SetTo(t2);

        // Issue a brand-new token by logging in again
        var secondLogin = await LoginAsync();
        var tokenC      = secondLogin.RefreshToken;

        using var scope = _factory.Services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var hashB  = RefreshTokenService.HashToken(tokenB);
        var hashC  = RefreshTokenService.HashToken(tokenC);
        var dbB    = await db.RefreshTokens.FirstAsync(t => t.TokenHash == hashB);
        var dbC    = await db.RefreshTokens.FirstAsync(t => t.TokenHash == hashC);

        dbB.ExpiresAt.Should().BeCloseTo(t1.AddDays(7), TimeSpan.FromSeconds(5),
            "Token B was issued at T1 so its expiry should be T1+7d");
        dbC.ExpiresAt.Should().BeCloseTo(t2.AddDays(7), TimeSpan.FromSeconds(5),
            "Token C was issued at T2 (after clock advance) so its expiry should be T2+7d");
        dbC.ExpiresAt.Should().BeAfter(dbB.ExpiresAt,
            "Token C was issued 30 days later so it must expire later");
    }

    // 3 — collection item AddedAt reflects FakeClock, not wall-clock
    [Fact]
    public async Task AddItemToCollection_AddedAt_ReflectsFakeClock()
    {
        var fakeNow = new DateTimeOffset(2023, 3, 15, 9, 30, 0, TimeSpan.Zero);
        _factory.Clock.SetTo(fakeNow);

        // Create a collection and a quote to add
        var collectionResponse = await _client.PostAsJsonAsync("/api/collections",
            new { name = "Clock Test Collection", ownerId = "user-1" });
        collectionResponse.EnsureSuccessStatusCode();
        var collectionBody = await collectionResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var collectionId   = collectionBody.GetProperty("id").GetInt32();

        var quoteId = await DbSeeder.SeedQuoteAsync(_factory.Services, "Seneca", "Omnia aliena sunt.");

        // Add the quote
        var addResponse = await _client.PostAsJsonAsync($"/api/collections/{collectionId}/items",
            new { quoteId });
        addResponse.EnsureSuccessStatusCode();

        // Read AddedAt from the API response body.
        // Reading owned items directly from DB via Include requires the EF navigation name,
        // but Collection uses a private backing field (_items) making that fragile.
        // The response body is authoritative and simpler.
        var body  = await addResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var items = body.GetProperty("items");
        items.GetArrayLength().Should().Be(1);

        var addedAt = items[0].GetProperty("addedAt").GetDateTime();
        addedAt.Should().BeCloseTo(fakeNow.UtcDateTime, TimeSpan.FromSeconds(5),
            "AddedAt must come from IClock.UtcNow, not real time");
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
