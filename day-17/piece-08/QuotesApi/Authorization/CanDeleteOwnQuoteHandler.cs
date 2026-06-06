using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace QuotesApi.Authorization;

/// <summary>
/// Enforces that the authenticated user can only delete their own quotes.
///
/// How it works with RequireAuthorization in Minimal APIs:
///   When an endpoint uses .RequireAuthorization("can-delete-own-quote"), ASP.NET Core's
///   authorization middleware passes the current HttpContext as context.Resource.
///   The handler reads the route "id", fetches the quote, and compares OwnerId to the
///   caller's "sub" claim.
/// </summary>
public sealed class CanDeleteOwnQuoteHandler : AuthorizationHandler<CanDeleteOwnQuoteRequirement>
{
    private readonly IQuoteRepository _quotes;
    private readonly ILogger<CanDeleteOwnQuoteHandler> _logger;

    public CanDeleteOwnQuoteHandler(
        IQuoteRepository quotes,
        ILogger<CanDeleteOwnQuoteHandler> logger)
    {
        _quotes = quotes;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanDeleteOwnQuoteRequirement requirement)
    {
        // In Minimal API pipelines, context.Resource is the HttpContext.
        if (context.Resource is not HttpContext http)
        {
            context.Fail();
            return;
        }

        // Parse caller's user id from the "sub" claim.
        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(sub, out var callerId))
        {
            context.Fail();
            return;
        }

        // Parse the {id} route segment.
        if (!http.GetRouteData().Values.TryGetValue("id", out var rawId)
            || !int.TryParse(rawId?.ToString(), out var quoteId))
        {
            context.Fail();
            return;
        }

        var ct = http.RequestAborted;
        var quote = await _quotes.GetByIdAsync(quoteId, ct);

        if (quote is null)
        {
            // Quote does not exist — succeed so the endpoint can return 404.
            context.Succeed(requirement);
            return;
        }

        // Null OwnerId means the quote was created before ownership tracking was added.
        // Treat as unowned (anyone with the write scope may delete).
        if (quote.OwnerId is null || quote.OwnerId == callerId)
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning(
                "Authorization denied: user {CallerId} attempted to delete quote {QuoteId} owned by user {OwnerId}",
                callerId, quoteId, quote.OwnerId);

            context.Fail();
        }
    }
}
