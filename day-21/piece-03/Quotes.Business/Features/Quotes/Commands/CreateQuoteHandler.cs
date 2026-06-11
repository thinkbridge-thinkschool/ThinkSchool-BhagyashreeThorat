using System.Text.Json;
using Quotes.Model.Messaging;

namespace Quotes.Business.Features.Quotes.Commands;

/// <summary>
/// Write path (command handler). Responsibilities, and ONLY these:
///   1. validation  — delegated to the <see cref="Quote"/> aggregate's invariants
///   2. normalized entity write — Add a tracked entity + SaveChanges
///
/// It returns just the new identifier (<see cref="Result{T}"/> of int), not a view
/// model. The write side has no idea what a screen wants to render — that is the
/// read side's job. Keeping the command focused on "change state" is the whole point.
/// </summary>
public sealed class CreateQuoteHandler
{
    private readonly AppDbContext _context;
    private readonly ILogger<CreateQuoteHandler> _logger;

    public CreateQuoteHandler(AppDbContext context, ILogger<CreateQuoteHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<int>> HandleAsync(CreateQuoteCommand command, CancellationToken cancellationToken)
    {
        // Validation stays on the write side. The aggregate owns its invariants
        // (author/text length), so the command handler never duplicates them.
        var result = Quote.Create(command.Author, command.Text,
            command.OwnerId > 0 ? command.OwnerId : null);

        if (!result.IsSuccess)
            return Result<int>.Fail(result.Error!);

        // Transactional outbox: the domain change (Quotes row) and the integration
        // event (OutboxMessages row) must both commit or neither does. We do NOT
        // publish to Service Bus here — a publish that happened before the commit
        // could leak an event for a quote that never persisted, and a publish after
        // the commit could be lost if the process dies in between. Instead the event
        // is durably staged in the same DB transaction; the OutboxRelayWorker publishes
        // it asynchronously and marks it sent.
        var quote = result.Value!;

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        // First save populates the identity-generated quote.Id so the event can
        // carry the real id. Both SaveChanges calls share one transaction, so the
        // commit is still all-or-nothing.
        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync(cancellationToken);

        var evt = new QuoteCreatedEvent
        {
            MessageId = Guid.NewGuid(),
            QuoteId = quote.Id,
            Author = quote.Author,
            Text = quote.Text,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Id == MessageId on purpose: the relay can use OutboxMessage.Id as the
        // Service Bus MessageId, so every redelivery of this row carries the same,
        // stable dedupe key the consumer keys ProcessedMessages on.
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = evt.MessageId,
            Type = nameof(QuoteCreatedEvent),
            Payload = JsonSerializer.Serialize(evt),
            CreatedAtUtc = evt.CreatedAtUtc
        });
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Quote {QuoteId} created by user {OwnerId} with author {Author}; outbox {MessageId} staged",
            quote.Id, command.OwnerId, quote.Author, evt.MessageId);

        return Result<int>.Ok(quote.Id);
    }
}
