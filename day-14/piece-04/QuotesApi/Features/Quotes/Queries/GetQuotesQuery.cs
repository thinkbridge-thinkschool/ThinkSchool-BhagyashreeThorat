namespace QuotesApi.Features.Quotes.Queries;

/// <summary>
/// Read-side intent: "give me a page of quotes, optionally matching a search term".
/// The query carries only what the read path needs to filter/page — nothing about
/// how state changes. It is paired with the projection DTO
/// <c>QuotesApi.DTOs.QuoteListItemDto</c> (Id, Author, Text), which is the exact
/// shape the list screen renders.
///
/// <paramref name="Search"/> is optional (null/blank = no filter). When present it
/// matches Author, Text, or — when the term is numeric — the quote Id. Filtering
/// runs in SQL BEFORE paging, so the page still respects Page/Size.
/// </summary>
public sealed record GetQuotesQuery(int Page, int Size, string? Search = null);
