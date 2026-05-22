using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Entities;
using Tests.Domain.Infrastructure;
using Xunit;

namespace Tests.Domain;

// ─────────────────────────────────────────────────────────────────────────────
//  Authorization integration tests
//
//  Verifies:
//    A) Claim-based policy  ("can-edit-quotes" requires scope=quotes.write)
//    B) Custom requirement  (CanDeleteOwnQuoteHandler — ownership check)
//
//  Key distinction under test:
//    401 Unauthorized  — identity unknown (middleware challenged)
//    403 Forbidden     — identity known but policy not satisfied
// ─────────────────────────────────────────────────────────────────────────────

[Collection("IntegrationTests")]
public class AuthorizationIntegrationTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly CustomWebApplicationFactory _factory;

    public AuthorizationIntegrationTests(SqlServerContainerFixture sqlFixture)
    {
        _factory = new CustomWebApplicationFactory(sqlFixture.ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    // ── A: Claim-based policy ─────────────────────────────────────────────── //

    // 1. No bearer token → authentication fails → 401
    [Fact]
    public async Task PostQuotes_NoToken_Returns401()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Dum differtur vita transcurrit." });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 2. Valid token with scope=quotes.write → policy satisfied → 201
    [Fact]
    public async Task PostQuotes_ValidScopeToken_Returns201()
    {
        var token = await GetAdminTokenAsync();

        var response = await CreateAuthenticatedClient(token)
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Dum differtur vita transcurrit." });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // 3. Valid token but scope claim absent → authenticated, policy fails → 403
    //
    //    This is the canonical 401-vs-403 distinction:
    //      401 = middleware challenged because identity is unknown
    //      403 = identity is known but RequireClaim("scope","quotes.write") fails
    [Fact]
    public async Task PostQuotes_MissingScopeClaim_Returns403()
    {
        var adminId       = await DbSeeder.GetUserIdAsync(_factory.Services, "admin@quotes.com");
        var scopelessToken = JwtTestHelper.BuildScopelessToken(adminId, "admin@quotes.com");

        var response = await CreateAuthenticatedClient(scopelessToken)
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Dum differtur vita transcurrit." });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // 4. Token owner deletes their own quote → CanDeleteOwnQuoteHandler succeeds → 204
    [Fact]
    public async Task DeleteQuote_OwnQuote_Returns204()
    {
        var token  = await GetAdminTokenAsync();
        var client = CreateAuthenticatedClient(token);

        var quoteId = await CreateQuoteViaApiAsync(client);

        var response = await client.DeleteAsync($"/api/quotes/{quoteId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // 4b. Expired access token → authentication fails → 401
    //
    //     Signature is valid, issuer/audience correct, but lifetime check
    //     rejects it (expired 15 min ago, past the 30-second ClockSkew).
    [Fact]
    public async Task PostQuotes_ExpiredAccessToken_Returns401()
    {
        var expiredToken = JwtTestHelper.BuildExpiredToken(1, "admin@quotes.com");

        var response = await CreateAuthenticatedClient(expiredToken)
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Dum differtur vita transcurrit." });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 5. Token owner tries to delete a quote owned by a different user
    //    → CanDeleteOwnQuoteHandler calls context.Fail() → 403
    [Fact]
    public async Task DeleteQuote_AnotherUsersQuote_Returns403()
    {
        var otherUserId = await DbSeeder.SeedUserAsync(_factory.Services, "other@quotes.com");
        var quoteId     = await DbSeeder.SeedQuoteAsync(_factory.Services,
            "Epictetus", "Make the best use of what is in your power.", otherUserId);

        var adminToken = await GetAdminTokenAsync();
        var response   = await CreateAuthenticatedClient(adminToken)
            .DeleteAsync($"/api/quotes/{quoteId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers ──────────────────────────────────────────────────────────── //

    private HttpClient CreateAuthenticatedClient(string bearerToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", bearerToken);
        return client;
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login",
                new { email = "admin@quotes.com", password = "Password123!" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        body.Should().NotBeNull();
        return body!.AccessToken;
    }

    private async Task<int> CreateQuoteViaApiAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Marcus Aurelius", text = "You have power over your mind." });

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return json.GetProperty("id").GetInt32();
    }
}
