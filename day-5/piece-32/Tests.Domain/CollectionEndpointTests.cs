using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tests.Domain.Infrastructure;
using Xunit;

namespace Tests.Domain;

// ─────────────────────────────────────────────────────────────────────────────
//  Collection endpoint integration tests
//
//  Covers the HTTP paths that were NOT exercised by ValidationAndErrorTests:
//    A) GET  /api/collections/{id} — found and not-found
//    B) DELETE /api/collections/{id} — found (204) and not-found (404)
//    C) POST /api/collections/{id}/items — collection not found (404)
//    D) DELETE /api/collections/{id}/items/{quoteId} — collection not found (404)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("IntegrationTests")]
public class CollectionEndpointTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CollectionEndpointTests(SqlServerContainerFixture sqlFixture)
    {
        _factory = new CustomWebApplicationFactory(sqlFixture.ConnectionString);
        _client  = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── A: GET /api/collections/{id} ─────────────────────────────────────── //

    [Fact]
    public async Task GetCollection_ExistingId_Returns200WithCollectionAndItems()
    {
        // Arrange — create a collection and add a quote to it
        var createResponse = await _client.PostAsJsonAsync("/api/collections",
            new { name = "Stoic Wisdom", ownerId = "user-1" });
        createResponse.EnsureSuccessStatusCode();
        var collectionBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var collectionId   = collectionBody.GetProperty("id").GetInt32();

        var quoteId = await DbSeeder.SeedQuoteAsync(_factory.Services, "Epictetus",
            "He is a wise man who does not grieve.");

        await _client.PostAsJsonAsync($"/api/collections/{collectionId}/items",
            new { quoteId });

        // Act
        var response = await _client.GetAsync($"/api/collections/{collectionId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("id").GetInt32().Should().Be(collectionId);
        body.GetProperty("name").GetString().Should().Be("Stoic Wisdom");
        body.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetCollection_NonExistentId_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/api/collections/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── B: DELETE /api/collections/{id} ──────────────────────────────────── //

    [Fact]
    public async Task DeleteCollection_ExistingId_Returns204()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/collections",
            new { name = "Temporary", ownerId = "user-1" });
        createResponse.EnsureSuccessStatusCode();
        var body = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id   = body.GetProperty("id").GetInt32();

        // Act
        var response = await _client.DeleteAsync($"/api/collections/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCollection_NonExistentId_Returns404()
    {
        // Act
        var response = await _client.DeleteAsync("/api/collections/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteCollection_ThenGetById_Returns404()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/collections",
            new { name = "DeleteMe", ownerId = "user-1" });
        createResponse.EnsureSuccessStatusCode();
        var body = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id   = body.GetProperty("id").GetInt32();

        await _client.DeleteAsync($"/api/collections/{id}");

        // Act
        var getResponse = await _client.GetAsync($"/api/collections/{id}");

        // Assert — hard-deleted, must return 404
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── C: POST /api/collections/{id}/items — collection not found ────────── //

    [Fact]
    public async Task AddItemToCollection_NonExistentCollection_Returns404()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/collections/99999/items",
            new { quoteId = 1 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── D: DELETE /api/collections/{id}/items/{quoteId} — collection not found //

    [Fact]
    public async Task RemoveItemFromCollection_NonExistentCollection_Returns404()
    {
        // Act
        var response = await _client.DeleteAsync("/api/collections/99999/items/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
