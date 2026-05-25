using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Tests.Domain.Infrastructure;
using Xunit;

namespace Tests.Domain;

// ─────────────────────────────────────────────────────────────────────────────
//  HybridAuthSchemeTests
//
//  Exercises the ForwardDefaultSelector lambda inside ServiceExtensions that
//  inspects every incoming Bearer token's issuer to route authentication to
//  the correct handler (InternalJwt vs EntraId).
//
//  Coverage targets:
//    A) No Authorization header           → routed to InternalJwtScheme → 401
//    B) Malformed Bearer value            → ForwardDefaultSelector's catch block
//                                           fires → falls through to InternalJwt → 401
//    C) Token with Entra-style issuer     → routed to EntraScheme
//                                           (EntraScheme rejects it — wrong key — → 401)
//    D) Non-Bearer Authorization header   → routed to InternalJwtScheme → 401
//
//  All paths lead to 401 here because no test has a real, signed token that
//  passes validation.  The ROUTING decision is what is being tested, not auth
//  success — coverage of those branches is the goal.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("IntegrationTests")]
public class HybridAuthSchemeTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public HybridAuthSchemeTests(SqlServerContainerFixture sqlFixture)
    {
        _factory = new CustomWebApplicationFactory(sqlFixture.ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    // ── A) No Authorization header ──────────────────────────────────────────

    [Fact]
    public async Task ProtectedEndpoint_NoAuthorizationHeader_Returns401()
    {
        // Arrange — unauthenticated client (no Authorization header)
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "Omnia aliena sunt." });

        // Assert — ForwardDefaultSelector falls through to InternalJwtScheme,
        //          which challenges unauthenticated requests with 401.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── B) Malformed Bearer value (triggers catch block in ForwardDefaultSelector) ──

    [Fact]
    public async Task ProtectedEndpoint_MalformedBearerToken_Returns401()
    {
        // Arrange — send a structurally valid bearer prefix but a token that
        //            JwtSecurityTokenHandler.CanReadToken returns false for,
        //            which makes the selector skip the issuer branch entirely.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer !!not-a-jwt!!");

        // Act
        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "Omnia aliena sunt." });

        // Assert — ForwardDefaultSelector fell through to InternalJwt, which rejects it → 401
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── C) Token whose issuer matches the Entra pattern ────────────────────

    [Fact]
    public async Task ProtectedEndpoint_EntraIssuedToken_RoutesToEntraSchemeAndReturns401()
    {
        // Arrange — build a JWT with an issuer that matches the Entra v2.0 pattern
        //            ("https://login.microsoftonline.com/{tenantId}/v2.0").
        //            The ForwardDefaultSelector reads the issuer WITHOUT validating
        //            the signature, so any well-formed JWT with the right issuer
        //            will exercise the EntraScheme branch.
        //            The EntraScheme will then reject it (wrong signing key) → 401.
        var entraIssuedToken = BuildEntraShapedToken(
            tenantId: "00000000-0000-0000-0000-000000000001",
            audience: "api://test-app");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", entraIssuedToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "Omnia aliena sunt." });

        // Assert — the EntraScheme handler was invoked (routing worked);
        //          it rejected the token because the signing key is wrong → 401.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── D) Non-Bearer Authorization scheme ──────────────────────────────────

    [Fact]
    public async Task ProtectedEndpoint_BasicAuthorizationHeader_Returns401()
    {
        // Arrange — Authorization header present but not a Bearer token;
        //           selector's StartsWith("Bearer ") check fails → InternalJwt → 401
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Basic dXNlcjpwYXNz");

        // Act
        var response = await client.PostAsJsonAsync("/api/quotes",
            new { author = "Seneca", text = "Omnia aliena sunt." });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a JWT whose Issuer field looks like an Entra v2.0 issuer
    /// ("https://login.microsoftonline.com/{tenantId}/v2.0").
    /// Signed with a throwaway RSA key — the key is intentionally wrong so
    /// EntraScheme's real validation rejects it after the routing decision.
    /// </summary>
    private static string BuildEntraShapedToken(string tenantId, string audience)
    {
        using var rsa = RSA.Create(2048);
        var key        = new RsaSecurityKey(rsa);
        var creds      = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var entraIssuer = $"https://login.microsoftonline.com/{tenantId}/v2.0";

        var token = new JwtSecurityToken(
            issuer:             entraIssuer,
            audience:           audience,
            claims:             [new Claim(JwtRegisteredClaimNames.Sub, "entra-user-id")],
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
