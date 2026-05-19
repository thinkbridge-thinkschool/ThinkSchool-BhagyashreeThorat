using Microsoft.AspNetCore.Mvc;
using QuotesApi.DTOs;
using QuotesApi.Entities;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (
            int page,
            int size,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quotes = await repository.GetAllAsync(
                page,
                size,
                cancellationToken);

            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        group.MapPost("/", async (
            [FromBody] CreateQuoteRequest request,
            IQuoteRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var validationErrors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Author))
            {
                validationErrors.Add(
                    nameof(request.Author),
                    ["Author is required"]);
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                validationErrors.Add(
                    nameof(request.Text),
                    ["Text is required"]);
            }

            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(validationErrors);
            }

            var quote = new Quote
            {
                Author = request.Author,
                Text = request.Text
            };

            var createdQuote = await repository.AddAsync(
                quote,
                cancellationToken);

            return Results.Created(
                $"/api/quotes/{createdQuote.Id}",
                createdQuote);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
                id,
                cancellationToken);

            return deleted
                ? Results.NoContent()
                : Results.NotFound();
        });

        return app;
    }
}