namespace QuotesApi.DTOs;

/// <summary>One row of the authors→quotes summary: an author and how many quotes they have.</summary>
public sealed record AuthorSummaryDto(int AuthorId, string Name, int QuoteCount, string? LatestQuote);
