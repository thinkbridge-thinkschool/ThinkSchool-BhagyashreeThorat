using Microsoft.AspNetCore.Mvc;
using QuotesApi.Abstractions;
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
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var result = Quote.Create(request.Author, request.Text);

            if (!result.IsSuccess)
                return Results.BadRequest(new { error = result.Error });

            var created = await repository.AddAsync(result.Value!, cancellationToken);

            return Results.Created($"/api/quotes/{created.Id}", created);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
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
            // Invariants (name length, non-empty) are enforced by the constructor.
            // If they are violated the aggregate throws ArgumentException,
            // which ExceptionMiddleware turns into a 400 ProblemDetails.
            Collection collection;
            try
            {
                collection = new Collection(request.Name, request.OwnerId);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid collection");
            }

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
            // Artificial delay so we can demonstrate cancellation in tests — remove before production
            await Task.Delay(5000, cancellationToken);

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
            // Duplicate QuoteId → InvalidOperationException → 400 via middleware.
            // Max 50 items       → InvalidOperationException → 400 via middleware.
            try
            {
                collection.AddItem(request.QuoteId, clock.UtcNow);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invariant violation");
            }

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

            try
            {
                collection.RemoveItem(quoteId);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invariant violation");
            }

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