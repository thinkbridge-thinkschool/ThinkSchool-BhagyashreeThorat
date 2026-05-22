namespace QuotesApi.DTOs;

public record CreateCollectionRequest(string Name, string OwnerId);

public record AddQuoteToCollectionRequest(int QuoteId);