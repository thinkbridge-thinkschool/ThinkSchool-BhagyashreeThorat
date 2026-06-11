using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Quotes.Model.Messaging;

namespace QuotesApi.Infrastructure.Messaging;

public sealed class ServiceBusPublisher
{
    private readonly ServiceBusClient _client;
    private readonly string _topicName;

    public ServiceBusPublisher(
        IOptions<ServiceBusOptions> options)
    {
        _client = new ServiceBusClient(
            options.Value.ConnectionString);

        _topicName = options.Value.TopicName;
    }

    public async Task PublishQuoteCreatedAsync(
        QuoteCreatedEvent quoteEvent,
        CancellationToken cancellationToken = default)
    {
        var sender = _client.CreateSender(_topicName);

        var message = new ServiceBusMessage(
            JsonSerializer.Serialize(quoteEvent))
        {
            MessageId = quoteEvent.MessageId.ToString()
        };

        await sender.SendMessageAsync(
            message,
            cancellationToken);
    }
}