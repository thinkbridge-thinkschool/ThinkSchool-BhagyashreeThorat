using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Entities;
using Xunit;

namespace Tests.Domain;

// ------------------------------------------------------------------ //
//  Authorization integration tests
//
//  Verifies:
//    A) Claim-based policy  ("can-edit-quotes" requires scope=quotes.write)
//    B) Custom requirement  (CanDeleteOwnQuoteHandler — ownership check)
//
//  Demonstrates the distinction between:
//    401 Unauthorized — not authenticated (no token / invalid token)
//    403 Forbidden    — authenticated but lacks permission
// ------------------------------------------------------------------ //

public class AuthorizationIntegrationTests : IClassFixture<AuthTestFactory>
{
    // Must match appsettings.json so hand-crafted tokens pass InternalJwtScheme validation.
    private const string JwtSecret   = "QuotesApi-Dev-SuperSecret-256bit-Key-ChangeInProd!";
    private const string JwtIssuer   = "QuotesApi";
    private const string JwtAudience = "QuotesApiUsers";

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly AuthTestFactory _factory;

    public AuthorizationIntegrationTests(AuthTestFactory factory) => _factory = factory;

    // ── A: Claim-based policy ─────────────────────────────────────── //

    // 1. No bearer token → authentication fails → 401
    [Fact]
    public async Task PostQuotes_NoToken_Returns401()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Dum differtur vita transcurrit." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 2. Valid token that carries scope=quotes.write → policy satisfied → 201
    [Fact]
    public async Task PostQuotes_ValidScopeToken_Returns201()
    {
        var token = await GetAdminTokenAsync();

        var response = await CreateAuthenticatedClient(token)
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Dum differtur vita transcurrit." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // 3. Valid token but scope claim is absent → authenticated, policy fails → 403
    //
    //    This is the key 401 vs 403 distinction:
    //      401 = identity unknown (middleware challenged)
    //      403 = identity known but RequireClaim("scope","quotes.write") not satisfied
    [Fact]
    public async Task PostQuotes_MissingScopeClaim_Returns403()
    {
        var adminId = await GetAdminIdAsync();
        var scopelessToken = BuildTokenWithoutScope(adminId, "admin@quotes.com");

        var response = await CreateAuthenticatedClient(scopelessToken)
            .PostAsJsonAsync("/api/quotes",
                new { author = "Seneca", text = "Dum differtur vita transcurrit." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── B: Custom requirement (ownership check) ───────────────────── //

    // 4. Token owner deletes their own quote → CanDeleteOwnQuoteHandler succeeds → 204
    [Fact]
    public async Task DeleteQuote_OwnQuote_Returns204()
    {
        var token = await GetAdminTokenAsync();
        var client = CreateAuthenticatedClient(token);

        var quoteId = await CreateQuoteViaApiAsync(client);

        var response = await client.DeleteAsync($"/api/quotes/{quoteId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // 5. Token owner tries to delete a quote owned by a different user
    //    → CanDeleteOwnQuoteHandler calls context.Fail() → 403
    [Fact]
    public async Task DeleteQuote_AnotherUsersQuote_Returns403()
    {
        var otherUserId = await SeedUserAsync("other@quotes.com");
        var quoteId     = await SeedQuoteOwnedByAsync(otherUserId);

        // Authenticate as admin (different user).
        var adminToken = await GetAdminTokenAsync();
        var response   = await CreateAuthenticatedClient(adminToken)
            .DeleteAsync($"/api/quotes/{quoteId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────── //

    private HttpClient CreateAuthenticatedClient(string bearerToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", bearerToken);
        return client;
    }

    // Logs in as the seeded admin and returns the access token (contains scope=quotes.write).
    private async Task<string> GetAdminTokenAsync()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login",
                new { email = "admin@quotes.com", password = "Password123!" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        Assert.NotNull(body);
        return body!.AccessToken;
    }

    private async Task<int> GetAdminIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Users.FirstAsync(u => u.Email == "admin@quotes.com")).Id;
    }

    // Builds a cryptographically valid JWT that is missing the scope claim.
    // The token passes signature and lifetime validation (InternalJwtScheme accepts it),
    // so the caller IS authenticated — but the "can-edit-quotes" policy still rejects it
    // because RequireClaim("scope","quotes.write") is not satisfied.
    private static string BuildTokenWithoutScope(int userId, string email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub,   userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
                // scope is intentionally absent — this is what triggers the 403
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Creates the user directly in the DB (no registration endpoint exists).
    // Idempotent: returns the existing user's ID if already seeded.
    private async Task<int> SeedUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null) return existing.Id;

        var user = User.Create(email, BCrypt.Net.BCrypt.HashPassword("Password123!"));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    // Seeds a quote owned by the given user ID directly via the DB context.
    private async Task<int> SeedQuoteOwnedByAsync(int ownerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = Quote.Create("Epictetus",
            "Make the best use of what is in your power.", ownerId);

        Assert.True(result.IsSuccess);
        db.Quotes.Add(result.Value!);
        await db.SaveChangesAsync();
        return result.Value!.Id;
    }

    // POSTs a quote through the API (exercises the full create path including OwnerId stamping).
    private async Task<int> CreateQuoteViaApiAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Marcus Aurelius", text = "You have power over your mind." });

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return json.GetProperty("id").GetInt32();
    }
}
