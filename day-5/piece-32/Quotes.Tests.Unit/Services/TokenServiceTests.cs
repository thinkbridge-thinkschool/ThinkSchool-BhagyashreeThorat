using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using QuotesApi.Entities;
using QuotesApi.Services;
using Xunit;

namespace Quotes.Tests.Unit.Services;

public class TokenServiceTests
{
    private readonly TokenService _sut;

    public TokenServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]   = "unit-test-secret-key-must-be-at-least-32-chars!",
                ["Jwt:Issuer"]   = "quotes-api-unit-test",
                ["Jwt:Audience"] = "quotes-api-clients-unit-test",
            })
            .Build();

        _sut = new TokenService(config);
    }

    // ──────────────────────────────────────────────────────────────────
    //  GenerateAccessToken
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_ValidUser_ReturnsWellFormedJwt()
    {
        // Arrange
        var user = User.Create("alice@example.com", "hashed");

        // Act
        var token = _sut.GenerateAccessToken(user);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        token.Split('.').Should().HaveCount(3, "a JWT has exactly three dot-separated parts");
    }

    [Fact]
    public void GenerateAccessToken_ValidUser_ContainsEmailClaim()
    {
        // Arrange
        var user = User.Create("alice@example.com", "hashed");

        // Act
        var token = _sut.GenerateAccessToken(user);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c =>
            c.Type == JwtRegisteredClaimNames.Email && c.Value == "alice@example.com");
    }

    [Fact]
    public void GenerateAccessToken_ValidUser_ContainsQuotesWriteScope()
    {
        // Arrange
        var user = User.Create("alice@example.com", "hashed");

        // Act
        var token = _sut.GenerateAccessToken(user);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "scope" && c.Value == "quotes.write");
    }

    [Fact]
    public void GenerateAccessToken_TwoDifferentUsers_ProduceDifferentTokens()
    {
        // Arrange
        var alice = User.Create("alice@example.com", "hashed");
        var bob   = User.Create("bob@example.com",   "hashed");

        // Act
        var tokenAlice = _sut.GenerateAccessToken(alice);
        var tokenBob   = _sut.GenerateAccessToken(bob);

        // Assert
        tokenAlice.Should().NotBe(tokenBob);
    }

    // ──────────────────────────────────────────────────────────────────
    //  GenerateRefreshToken
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_ReturnsValidBase64String()
    {
        // Arrange & Act
        var token = _sut.GenerateRefreshToken();

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        Action parse = () => Convert.FromBase64String(token);
        parse.Should().NotThrow("the token must be valid Base64");
    }

    [Fact]
    public void GenerateRefreshToken_CalledTwice_ReturnsDifferentTokens()
    {
        // Arrange & Act
        var first  = _sut.GenerateRefreshToken();
        var second = _sut.GenerateRefreshToken();

        // Assert
        first.Should().NotBe(second, "tokens are generated from cryptographic randomness");
    }
}
