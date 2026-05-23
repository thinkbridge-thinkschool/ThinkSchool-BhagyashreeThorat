using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Entities;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(
        this IEndpointRouteBuilder app)
    {
        MapQuotes(app);
        MapCollections(app);
        return app;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (
            LoginRequest request,
            AppDbContext db,
            ITokenService tokenService,
            IRefreshTokenService refreshTokenService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Auth");
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email.ToLowerInvariant(), cancellationToken);

            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                logger.LogWarning("Login failed for email {Email}", request.Email);
                return Results.Unauthorized();
            }

            var rawRefreshToken = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

            logger.LogInformation("Login successful for user {UserId} email {Email}", user.Id, user.Email);

            return Results.Ok(new LoginResponse
            {
                AccessToken = tokenService.GenerateAccessToken(user),
                RefreshToken = rawRefreshToken,
                ExpiresIn = 900
            });
        });

        group.MapPost("/refresh", async (
            RefreshRequest request,
            ITokenService tokenService,
            IRefreshTokenService refreshTokenService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Auth");
            try
            {
                var (newRawToken, user) = await refreshTokenService.RotateAsync(request.RefreshToken, cancellationToken);

                logger.LogInformation("Refresh token rotated for user {UserId}", user.Id);

                return Results.Ok(new LoginResponse
                {
                    AccessToken = tokenService.GenerateAccessToken(user),
                    RefreshToken = newRawToken,
                    ExpiresIn = 900
                });
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("Refresh token rotation failed: {Reason}", ex.Message);
                return Results.Unauthorized();
            }
        });

        group.MapPost("/logout", async (
            LogoutRequest request,
            IRefreshTokenService refreshTokenService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Auth");
            await refreshTokenService.RevokeAsync(request.RefreshToken, cancellationToken);
            logger.LogInformation("Refresh token revoked (logout)");
            return Results.NoContent();
        });

        return app;
    }

    // ------------------------------------------------------------------ //
    //  Quotes
    // ------------------------------------------------------------------ //

    private static void MapQuotes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (
            int page,
            int size,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quotes = await repository.GetAllAsync(page, size, cancellationToken);
            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(id, cancellationToken);
            return quote is null ? Results.NotFound() : Results.Ok(quote);
        });

        group.MapPost("/", async (
            [FromBody] CreateQuoteRequest request,
            HttpContext http,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");
            // Stamp the owner from the authenticated caller's "sub" claim.
            var sub = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            int.TryParse(sub, out var ownerId);

            var result = Quote.Create(request.Author, request.Text, ownerId > 0 ? ownerId : null);

            if (!result.IsSuccess)
                return Results.BadRequest(new { error = result.Error });

            var created = await repository.AddAsync(result.Value!, cancellationToken);

            logger.LogInformation(
                "Quote {QuoteId} created by user {OwnerId} with author {Author}",
                created.Id, ownerId, created.Author);

            return Results.Created($"/api/quotes/{created.Id}", created);
        }).RequireAuthorization(AuthorizationPolicies.CanEditQuotes);

        group.MapPut("/{id:int}", async (
            int id,
            [FromBody] UpdateQuoteRequest request,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(id, cancellationToken);
            if (quote is null)
                return Results.NotFound();

            var result = quote.Update(request.Author, request.Text);
            if (!result.IsSuccess)
                return Results.BadRequest(new { error = result.Error });

            await repository.UpdateAsync(quote, cancellationToken);
            return Results.Ok(quote);
        }).RequireAuthorization(AuthorizationPolicies.CanEditQuotes);

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");
            var deleted = await repository.DeleteAsync(id, cancellationToken);

            if (!deleted)
                return Results.NotFound();

            logger.LogInformation("Quote {QuoteId} deleted", id);
            return Results.NoContent();
        }).RequireAuthorization(AuthorizationPolicies.CanDeleteOwnQuote);
    }

    // ------------------------------------------------------------------ //
    //  Collections
    // ------------------------------------------------------------------ //

    private static void MapCollections(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections");

        // POST /api/collections — create a new collection
        group.MapPost("/", async (
            CreateCollectionRequest request,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            // Constructor enforces name invariants and throws DomainException on violation.
            // ExceptionMiddleware converts DomainException → 400 ProblemDetails.
            var collection = new Collection(request.Name, request.OwnerId);

            var created = await repository.AddAsync(collection, cancellationToken);

            return Results.Created($"/api/collections/{created.Id}", new
            {
                created.Id,
                created.Name,
                created.OwnerId,
                Items = created.Items
            });
        });

        // GET /api/collections/{id}
        group.MapGet("/{id:int}", async (
            int id,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(id, cancellationToken);

            return collection is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    collection.Id,
                    collection.Name,
                    collection.OwnerId,
                    Items = collection.Items
                });
        });

        // POST /api/collections/{id}/items — add a quote (all mutation via aggregate)
        group.MapPost("/{id:int}/items", async (
            int id,
            AddQuoteToCollectionRequest request,
            ICollectionRepository repository,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(id, cancellationToken);

            if (collection is null)
                return Results.NotFound();

            // Invariants enforced inside the aggregate root.
            // Duplicate QuoteId / max-50 → throws DomainException
            // → ExceptionMiddleware converts to 400 ProblemDetails.
            collection.AddItem(request.QuoteId, clock.UtcNow);

            await repository.UpdateAsync(collection, cancellationToken);

            return Results.Ok(new
            {
                collection.Id,
                collection.Name,
                Items = collection.Items
            });
        });

        // DELETE /api/collections/{id}/items/{quoteId} — remove a quote
        group.MapDelete("/{id:int}/items/{quoteId:int}", async (
            int id,
            int quoteId,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(id, cancellationToken);

            if (collection is null)
                return Results.NotFound();

            // DomainException thrown by RemoveItem if quoteId not present
            // → ExceptionMiddleware converts to 400 ProblemDetails.
            collection.RemoveItem(quoteId);

            await repository.UpdateAsync(collection, cancellationToken);

            return Results.NoContent();
        });

        // DELETE /api/collections/{id}
        group.MapDelete("/{id:int}", async (
            int id,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }
}
