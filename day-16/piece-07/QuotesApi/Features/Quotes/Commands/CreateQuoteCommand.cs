namespace QuotesApi.Features.Quotes.Commands;

/// <summary>
/// Write-side intent: "create a quote". This is the COMMAND model — it carries
/// only the fields needed to <b>change state</b>, in the shape the write path wants
/// (normalized: author + text + owner). It is deliberately not the same shape as
/// any read DTO. Commands describe a mutation; they do not return view data.
/// </summary>
public sealed record CreateQuoteCommand(string Author, string Text, int? OwnerId);
