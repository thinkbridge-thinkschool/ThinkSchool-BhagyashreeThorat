using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using QuotesApi.Authorization;
using Quotes.Model.Shared;
using Quotes.Repository.Entities;
using Quotes.Repository.Repositories;
using Xunit;

namespace Quotes.Tests.Unit.Authorization;

public class CanDeleteOwnQuoteHandlerTests
{
    private readonly IQuoteRepository          _quotes;
    private readonly CanDeleteOwnQuoteHandler  _sut;

    public CanDeleteOwnQuoteHandlerTests()
    {
        _quotes = Substitute.For<IQuoteRepository>();
        _sut    = new CanDeleteOwnQuoteHandler(
            _quotes,
            Substitute.For<ILogger<CanDeleteOwnQuoteHandler>>());
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a DefaultHttpContext with route "id" set and an optional "sub" claim.
    /// </summary>
    private static DefaultHttpContext BuildHttpContext(int? callerId, int quoteId)
    {
        var http = new DefaultHttpContext();

        var routeData = new RouteData();
        routeData.Values["id"] = quoteId.ToString();
        http.Features.Set<IRoutingFeature>(new TestRoutingFeature(routeData));

        if (callerId.HasValue)
        {
            var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()) };
            http.User  = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        return http;
    }

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal user, object resource)
    {
        return new AuthorizationHandlerContext(
            requirements: [new CanDeleteOwnQuoteRequirement()],
            user:         user,
            resource:     resource);
    }

    // Minimal IRoutingFeature so we can inject RouteData without relying on internals.
    private sealed class TestRoutingFeature : IRoutingFeature
    {
        public TestRoutingFeature(RouteData routeData) => RouteData = routeData;
        public RouteData? RouteData { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Guard conditions
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleRequirement_ResourceIsNotHttpContext_Fails()
    {
        // Arrange — pass a non-HttpContext resource
        var context = BuildContext(new ClaimsPrincipal(), resource: "not_an_http_context");

        // Act
        await _sut.HandleAsync(context);

        // Assert
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirement_MissingSubClaim_Fails()
    {
        // Arrange — authenticated user but no "sub" claim
        var http    = BuildHttpContext(callerId: null, quoteId: 1);
        var context = BuildContext(new ClaimsPrincipal(new ClaimsIdentity()), http);

        // Act
        await _sut.HandleAsync(context);

        // Assert
        context.HasFailed.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Quote-not-found: let the endpoint return 404 rather than blocking here
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleRequirement_QuoteNotFound_Succeeds()
    {
        // Arrange
        _quotes.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns((Quote?)null);

        var http    = BuildHttpContext(callerId: 5, quoteId: 999);
        var context = BuildContext(http.User, http);

        // Act
        await _sut.HandleAsync(context);

        // Assert — succeed so the endpoint can emit 404 rather than 403
        context.HasSucceeded.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Ownership checks
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleRequirement_CallerOwnsQuote_Succeeds()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text", ownerId: 7).Value!;
        _quotes.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(quote);

        var http    = BuildHttpContext(callerId: 7, quoteId: 1); // same user
        var context = BuildContext(http.User, http);

        // Act
        await _sut.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirement_CallerDoesNotOwnQuote_Fails()
    {
        // Arrange — quote owned by user 99; caller is user 5
        var quote = Quote.Create("Author", "Text", ownerId: 99).Value!;
        _quotes.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(quote);

        var http    = BuildHttpContext(callerId: 5, quoteId: 1);
        var context = BuildContext(http.User, http);

        // Act
        await _sut.HandleAsync(context);

        // Assert
        context.HasFailed.Should().BeTrue("caller does not own this quote — must get 403");
    }

    [Fact]
    public async Task HandleRequirement_LegacyUnownedQuote_Succeeds()
    {
        // Arrange — OwnerId is null: created before ownership tracking was added
        var quote = Quote.Create("Author", "Text", ownerId: null).Value!;
        _quotes.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(quote);

        var http    = BuildHttpContext(callerId: 5, quoteId: 1);
        var context = BuildContext(http.User, http);

        // Act
        await _sut.HandleAsync(context);

        // Assert — anyone with the write scope may delete an unowned legacy quote
        context.HasSucceeded.Should().BeTrue();
    }
}
