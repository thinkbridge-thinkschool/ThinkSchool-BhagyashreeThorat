using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authorization;

namespace Tests.Domain.Infrastructure;

/// <summary>
/// Generates hand-crafted JWTs for integration tests.
///
/// All constants match appsettings.json so tokens pass the real
/// InternalJwtScheme validation (correct signature, issuer, audience).
/// Tests deliberately vary lifetime and claims to exercise auth edge cases.
/// </summary>
public static class JwtTestHelper
{
    // Must match appsettings.json — these are the values the real JWT middleware validates.
    public const string Secret   = "QuotesApi-Dev-SuperSecret-256bit-Key-ChangeInProd!";
    public const string Issuer   = "QuotesApi";
    public const string Audience = "QuotesApiUsers";

    /// <summary>
    /// Builds a structurally valid, signed JWT.
    /// </summary>
    /// <param name="userId">Written into the "sub" claim.</param>
    /// <param name="email">Written into the "email" claim.</param>
    /// <param name="includeScope">When true, adds scope=quotes.write required by edit/delete policies.</param>
    /// <param name="notBefore">Token valid-from time (defaults to now).</param>
    /// <param name="expires">Token expiry (defaults to now + 15 min).</param>
    public static string BuildToken(
        int            userId,
        string         email,
        bool           includeScope = true,
        DateTimeOffset? notBefore   = null,
        DateTimeOffset? expires     = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var now = DateTimeOffset.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };

        if (includeScope)
            claims.Add(new Claim(AppClaimTypes.Scope, "quotes.write"));

        var token = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           Audience,
            claims:             claims,
            notBefore:          (notBefore ?? now).UtcDateTime,
            expires:            (expires ?? now.AddMinutes(15)).UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Builds a token that expired 15 minutes ago (past the 30-second ClockSkew).
    /// The signature is valid — so the middleware recognises it as InternalJwt —
    /// but ValidateLifetime = true rejects it, yielding a 401.
    /// </summary>
    public static string BuildExpiredToken(int userId, string email) =>
        BuildToken(
            userId,
            email,
            includeScope: true,
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-30),
            expires:   DateTimeOffset.UtcNow.AddMinutes(-15));

    /// <summary>
    /// Valid token that is missing the scope claim.
    /// Authentication succeeds (identity known) but policies requiring
    /// scope=quotes.write will fail → 403 Forbidden.
    /// </summary>
    public static string BuildScopelessToken(int userId, string email) =>
        BuildToken(userId, email, includeScope: false);
}
