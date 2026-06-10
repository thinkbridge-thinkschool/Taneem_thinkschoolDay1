using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace QuotesApi.ServiceBus;

public class QuoteCreatedPublisher
{
    private readonly ServiceBusSender? _sender;

    public QuoteCreatedPublisher(ServiceBusClient? client, IConfiguration config)
    {
        var topicName = config["ServiceBus:TopicName"];
        if (client != null && !string.IsNullOrEmpty(topicName))
            _sender = client.CreateSender(topicName);
    }

    // Called by OutboxRelay — publishes a raw payload already serialised into the outbox
    public async Task PublishRawAsync(string eventType, string payload, string messageId, CancellationToken ct)
    {
        if (_sender is null) return;

        var message = new ServiceBusMessage(payload)
        {
            MessageId   = messageId,
            ContentType = "application/json",
            Subject     = eventType
        };

        await _sender.SendMessageAsync(message, ct);
    }
}
