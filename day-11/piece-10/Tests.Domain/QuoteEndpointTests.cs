using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tests.Domain.Infrastructure;
using Xunit;

namespace Tests.Domain;

// ─────────────────────────────────────────────────────────────────────────────
//  Quote endpoint integration tests
//
//  Covers: GET, POST, PUT, DELETE — success paths, failure paths,
//          401/403 auth failures, and domain validation rejections.
//
//  Each test method gets a fresh CustomWebApplicationFactory (xUnit creates a
//  new class instance per test), so a completely isolated SQLite in-memory DB.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("IntegrationTests")]
public class QuoteEndpointTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public QuoteEndpointTests(SqlServerContainerFixture sqlFixture)
    {
        _factory = new CustomWebApplicationFactory(sqlFixture.ConnectionString);
        _client  = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GET /api/quotes
    // ════════════════════════════════════════════════════════════════════════

    // 1 — empty DB returns empty list (public endpoint, no auth required)
    [Fact]
    public async Task GetQuotes_EmptyDatabase_Returns200WithEmptyList()
    {
        var response = await _client.GetAsync("/api/quotes?page=1&size=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts);
        body.Should().NotBeNull().And.BeEmpty();
    }

    // 2 — after seeding quotes, GET returns them (proves EF persistence end-to-end)
    [Fact]
    public async Task GetQuotes_AfterSeedingData_ReturnsSeededQuotes()
    {
        await DbSeeder.SeedQuoteAsync(_factory.Services, "Marcus Aurelius",
            "Waste no more time arguing what a good man should be.");
        await DbSeeder.SeedQuoteAsync(_factory.Services, "Epictetus",
            "No man is free who is not master of himself.");

        var response = await _client.GetAsync("/api/quotes?page=1&size=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts);
        body.Should().HaveCount(2);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GET /api/quotes/{id}
    // ════════════════════════════════════════════════════════════════════════

    // 3 — known ID returns the correct quote
    [Fact]
    public async Task GetQuoteById_ExistingQuote_Returns200WithQuote()
    {
        var quoteId = await DbSeeder.SeedQuoteAsync(_factory.Services, "Seneca",
            "Dum differtur vita transcurrit.");

        var response = await _client.GetAsync($"/api/quotes/{quoteId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("id").GetInt32().Should().Be(quoteId);
        body.GetProperty("author").GetString().Should().Be("Seneca");
        body.GetProperty("text").GetString().Should().Be("Dum differtur vita transcurrit.");
    }

    // 4 — non-existent ID returns 404
    [Fact]
    public async Task GetQuoteById_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync("/api/quotes/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  POST /api/quotes  (requires can-edit-quotes policy)
    // ════════════════════════════════════════════════════════════════════════

    // 5 — authenticated request with scope → 201 Created with quote payload
    [Fact]
    public async Task PostQuote_WithValidScopeToken_Returns201WithCreatedQuote()
    {
        var token = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Marcus Aurelius", text = "You have power over your mind." });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull("Created must include a Location header");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("author").GetString().Should().Be("Marcus Aurelius");
        body.GetProperty("text").GetString().Should().Be("You have power over your mind.");
        body.GetProperty("id").GetInt32().Should().BePositive();
    }

    // 6 — no bearer token → 401 (real auth middleware challenge)
    [Fact]
    public async Task PostQuote_WithoutToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "Per aspera ad astra." });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 7 — structurally valid but expired token → 401 (lifetime validation)
    [Fact]
    public async Task PostQuote_WithExpiredToken_Returns401()
    {
        var adminId      = await DbSeeder.GetUserIdAsync(_factory.Services, "admin@quotes.com");
        var expiredToken = JwtTestHelper.BuildExpiredToken(adminId, "admin@quotes.com");

        var response = await AuthenticatedClient(expiredToken)
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Per aspera ad astra." });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 8 — authenticated but scope claim missing → 403 (policy evaluation)
    [Fact]
    public async Task PostQuote_WithoutScopeClaim_Returns403()
    {
        var adminId       = await DbSeeder.GetUserIdAsync(_factory.Services, "admin@quotes.com");
        var scopelessToken = JwtTestHelper.BuildScopelessToken(adminId, "admin@quotes.com");

        var response = await AuthenticatedClient(scopelessToken)
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Per aspera ad astra." });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // 9 — domain validation failure (empty author) → 400 (Quote.Create returns Fail)
    [Fact]
    public async Task PostQuote_WithEmptyAuthor_Returns400()
    {
        var token  = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "", text = "Some valid text." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 10 — domain validation failure (empty text) → 400
    [Fact]
    public async Task PostQuote_WithEmptyText_Returns400()
    {
        var token  = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PUT /api/quotes/{id}  (requires can-edit-quotes policy)
    // ════════════════════════════════════════════════════════════════════════

    // 11 — authenticated update of existing quote → 200 with updated values
    [Fact]
    public async Task PutQuote_WithValidToken_Returns200WithUpdatedQuote()
    {
        var quoteId = await DbSeeder.SeedQuoteAsync(_factory.Services,
            "Original Author", "Original text.");

        var token  = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var response = await client.PutAsJsonAsync($"/api/quotes/{quoteId}",
            new { author = "Updated Author", text = "Updated text." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("author").GetString().Should().Be("Updated Author");
        body.GetProperty("text").GetString().Should().Be("Updated text.");
    }

    // 12 — PUT on non-existent quote → 404
    [Fact]
    public async Task PutQuote_NonExistentId_Returns404()
    {
        var token  = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var response = await client.PutAsJsonAsync("/api/quotes/99999",
            new { author = "Someone", text = "Some text." });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  DELETE /api/quotes/{id}  (requires can-delete-own-quote policy)
    // ════════════════════════════════════════════════════════════════════════

    // 13 — DELETE non-existent quote → 404
    //      The CanDeleteOwnQuoteHandler lets not-found quotes through
    //      (the endpoint itself returns 404); this verifies that path.
    [Fact]
    public async Task DeleteQuote_NonExistentId_Returns404()
    {
        var adminId = await DbSeeder.GetUserIdAsync(_factory.Services, "admin@quotes.com");
        var token   = JwtTestHelper.BuildToken(adminId, "admin@quotes.com");
        var client  = AuthenticatedClient(token);

        var response = await client.DeleteAsync("/api/quotes/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 14 — DELETE without token → 401
    [Fact]
    public async Task DeleteQuote_WithoutToken_Returns401()
    {
        var quoteId = await DbSeeder.SeedQuoteAsync(_factory.Services,
            "Epictetus", "Freedom is not procured by a full enjoyment of what is desired.");

        var response = await _client.DeleteAsync($"/api/quotes/{quoteId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 15 — Persistence round-trip: POST then GET returns the same quote
    [Fact]
    public async Task PostQuote_ThenGetById_ReturnsSameQuote()
    {
        var token  = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var createResponse = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Aurelius", text = "The impediment to action advances action." });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id      = created.GetProperty("id").GetInt32();

        var getResponse = await _client.GetAsync($"/api/quotes/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        fetched.GetProperty("id").GetInt32().Should().Be(id);
        fetched.GetProperty("author").GetString().Should().Be("Aurelius");
    }

    // ── Helpers ──────────────────────────────────────────────────────────── //

    private async Task<string> LoginAsAdminAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@quotes.com", password = "Password123!" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return body.GetProperty("accessToken").GetString()!;
    }

    private HttpClient AuthenticatedClient(string bearerToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", bearerToken);
        return client;
    }
}
