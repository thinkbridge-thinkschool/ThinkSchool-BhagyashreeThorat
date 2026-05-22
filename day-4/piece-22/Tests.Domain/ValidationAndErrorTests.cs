using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tests.Domain.Infrastructure;
using Xunit;

namespace Tests.Domain;

// ─────────────────────────────────────────────────────────────────────────────
//  Validation and error-response integration tests
//
//  Verifies that the REAL pipeline correctly rejects bad input and returns
//  the expected status codes and response shapes (including ProblemDetails).
//
//  Two kinds of validation failure exist in this API:
//
//    A) Domain validation via Quote.Create → Result.Fail →
//       Results.BadRequest(new { error }) — custom 400 object
//
//    B) Aggregate invariant violations (Collection constructor/methods) →
//       Results.Problem(...) → RFC 7807 ProblemDetails with
//       type, title, status, detail fields
// ─────────────────────────────────────────────────────────────────────────────

[Collection("IntegrationTests")]
public class ValidationAndErrorTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ValidationAndErrorTests(SqlServerContainerFixture sqlFixture)
    {
        _factory = new CustomWebApplicationFactory(sqlFixture.ConnectionString);
        _client  = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── A: Domain validation (Quote) ─────────────────────────────────────── //

    // 1 — author empty string → Quote.Create returns Fail → 400
    [Fact]
    public async Task PostQuote_WithEmptyAuthor_Returns400WithErrorMessage()
    {
        var token  = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "", text = "Valid text content." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("error").GetString().Should()
            .NotBeNullOrEmpty("domain validation must return an error message");
    }

    // 2 — author exceeds 200-char limit → Quote.Create returns Fail → 400
    [Fact]
    public async Task PostQuote_WithAuthorTooLong_Returns400()
    {
        var token  = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var tooLongAuthor = new string('A', 201);
        var response      = await client.PostAsJsonAsync("/api/quotes",
            new { author = tooLongAuthor, text = "Valid text content." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 3 — text exceeds 1000-char limit → 400
    [Fact]
    public async Task PostQuote_WithTextTooLong_Returns400()
    {
        var token  = await LoginAsAdminAsync();
        var client = AuthenticatedClient(token);

        var tooLongText = new string('T', 1001);
        var response    = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = tooLongText });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── B: ProblemDetails (Collection invariants) ─────────────────────────── //

    // 4 — collection name shorter than 3 chars → ArgumentException in constructor
    //     → Results.Problem(...)  → ProblemDetails shape with status 400
    [Fact]
    public async Task CreateCollection_WithNameTooShort_Returns400WithProblemDetails()
    {
        var response = await _client.PostAsJsonAsync("/api/collections",
            new { name = "AB", ownerId = "user-1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        // ProblemDetails has a "status" field equal to the HTTP status code
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("Domain Rule Violation");
        body.GetProperty("detail").GetString().Should().NotBeNullOrEmpty();
    }

    // 5 — collection name longer than 80 chars → 400 ProblemDetails
    [Fact]
    public async Task CreateCollection_WithNameTooLong_Returns400WithProblemDetails()
    {
        var tooLongName = new string('X', 81);
        var response    = await _client.PostAsJsonAsync("/api/collections",
            new { name = tooLongName, ownerId = "user-1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("status").GetInt32().Should().Be(400);
    }

    // 6 — adding the same quote twice to a collection → invariant violation
    //     → Results.Problem(...) → 400 ProblemDetails
    [Fact]
    public async Task AddItemToCollection_DuplicateQuote_Returns400WithProblemDetails()
    {
        // Create a collection
        var createResponse = await _client.PostAsJsonAsync("/api/collections",
            new { name = "My Collection", ownerId = "user-1" });
        createResponse.EnsureSuccessStatusCode();
        var collectionBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var collectionId   = collectionBody.GetProperty("id").GetInt32();

        // Seed a quote
        var quoteId = await DbSeeder.SeedQuoteAsync(_factory.Services, "Author", "Text.");

        // Add it once — should succeed
        var firstAdd = await _client.PostAsJsonAsync($"/api/collections/{collectionId}/items",
            new { quoteId });
        firstAdd.StatusCode.Should().Be(HttpStatusCode.OK);

        // Add the same quote again — should be rejected
        var secondAdd = await _client.PostAsJsonAsync($"/api/collections/{collectionId}/items",
            new { quoteId });

        secondAdd.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await secondAdd.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("Domain Rule Violation");
    }

    // ── C: Auth error responses ───────────────────────────────────────────── //

    // 7 — garbage refresh token → 401 (not a 500 — the service handles it gracefully)
    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = "this-is-not-a-valid-token" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 8 — removing an item that is not in the collection → 400 ProblemDetails
    [Fact]
    public async Task RemoveItemFromCollection_ItemNotPresent_Returns400WithProblemDetails()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/collections",
            new { name = "Empty Collection", ownerId = "user-1" });
        createResponse.EnsureSuccessStatusCode();
        var collectionBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var collectionId   = collectionBody.GetProperty("id").GetInt32();

        var response = await _client.DeleteAsync($"/api/collections/{collectionId}/items/9999");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("status").GetInt32().Should().Be(400);
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
