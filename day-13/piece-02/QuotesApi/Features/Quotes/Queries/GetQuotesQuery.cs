namespace QuotesApi.Features.Quotes.Queries;

/// <summary>
/// Read-side intent: "give me a page of quotes". The query carries only what the
/// read path needs to filter/page — nothing about how state changes. It is paired
/// with the projection DTO <c>QuotesApi.DTOs.QuoteListItemDto</c> (Id, Author, Text),
/// which is the exact shape the list screen renders.
/// </summary>
public sealed record GetQuotesQuery(int Page, int Size);
