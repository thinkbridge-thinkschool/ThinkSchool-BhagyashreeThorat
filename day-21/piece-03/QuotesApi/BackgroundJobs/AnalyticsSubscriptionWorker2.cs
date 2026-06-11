using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Infrastructure.Messaging;
using Quotes.Model.Messaging;

namespace QuotesApi.BackgroundJobs;

public sealed class AnalyticsSubscriptionWorker2 : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<AnalyticsSubscriptionWorker> _logger;

    public AnalyticsSubscriptionWorker2(
        IOptions<ServiceBusOptions> options,
        ILogger<AnalyticsSubscriptionWorker> logger)
    {
        _client = new ServiceBusClient(
            options.Value.ConnectionString);

        _logger = logger;
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

        _logger.LogInformation("Analytics Worker 2 started");

        await Task.Delay(
            Timeout.Infinite,
            stoppingToken);
    }

    private async Task ProcessMessage(
        ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();

        var quoteEvent =
            JsonSerializer.Deserialize<QuoteCreatedEvent>(body);

        _logger.LogInformation(
            "PID={Pid} processed MessageId={MessageId}",
            Environment.ProcessId,
            quoteEvent?.MessageId);

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