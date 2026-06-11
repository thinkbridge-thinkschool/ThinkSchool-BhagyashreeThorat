using QuotesApi.Infrastructure.Messaging;
using Quotes.Model.Messaging;

namespace QuotesApi.Endpoints;

public static class ServiceBusEndpoints
{
    public static IEndpointRouteBuilder MapServiceBusEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/servicebus/test-publish",
            async (ServiceBusPublisher publisher) =>
            {
                var quoteEvent = new QuoteCreatedEvent
                {
                    MessageId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    QuoteId = 0,
                    Author = "Bhagyashree",
                    Text = "Service Bus Test",
                    CreatedAtUtc = DateTime.UtcNow
                };

                await publisher.PublishQuoteCreatedAsync(quoteEvent);

                return Results.Ok(new
                {
                    Message = "Published",
                    quoteEvent.MessageId
                });
            });

        return endpoints;
    }
}