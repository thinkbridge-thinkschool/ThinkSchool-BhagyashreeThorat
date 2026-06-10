using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotes.Repository.Context;
using Quotes.Repository.Entities;
using QuotesApi.Infrastructure.Messaging;
using Quotes.Model.Messaging;

namespace QuotesApi.BackgroundJobs;

public sealed class AnalyticsSubscriptionWorker : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<AnalyticsSubscriptionWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public AnalyticsSubscriptionWorker(
        IOptions<ServiceBusOptions> options,
        ILogger<AnalyticsSubscriptionWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        _client = new ServiceBusClient(
            options.Value.ConnectionString);
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var processor = _client.CreateProcessor(
            "quote-events",
            "analytics-sub",
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false
            });

        processor.ProcessMessageAsync += ProcessMessage;
        processor.ProcessErrorAsync += ProcessError;

        await processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation(
            "Analytics consumer started");

        await Task.Delay(
            Timeout.Infinite,
            stoppingToken);
    }

    private async Task ProcessMessage(
        ProcessMessageEventArgs args)
    {
        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var messageId = Guid.Parse(args.Message.MessageId);

        var alreadyProcessed = await db.ProcessedMessages
            .AnyAsync(x => x.MessageId == messageId);

        if (alreadyProcessed)
        {
            _logger.LogWarning(
                "Duplicate message ignored {MessageId}",
                messageId);

            await args.CompleteMessageAsync(args.Message);

            return;
        }

        var body = args.Message.Body.ToString();

        var quoteEvent =
            JsonSerializer.Deserialize<QuoteCreatedEvent>(body);

        if (quoteEvent?.Author == "POISON")
        {
            throw new InvalidOperationException(
                "Poison message detected");
        }

        _logger.LogInformation(
            "Analytics received QuoteId={QuoteId} MessageId={MessageId}",
            quoteEvent?.QuoteId,
            quoteEvent?.MessageId);

        db.ProcessedMessages.Add(
            new ProcessedMessage
            {
                MessageId = messageId,
                ProcessedAtUtc = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        await args.CompleteMessageAsync(args.Message);
    }

    private Task ProcessError(
        ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "ServiceBus consumer error");

        return Task.CompletedTask;
    }
}