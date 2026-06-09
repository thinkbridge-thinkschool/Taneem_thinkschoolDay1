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

    public async Task PublishAsync(int quoteId, string author, CancellationToken ct)
    {
        if (_sender is null) return;

        var payload = JsonSerializer.Serialize(new { quoteId, author });

        var message = new ServiceBusMessage(payload)
        {
            MessageId   = $"quote-created-{quoteId}",
            ContentType = "application/json",
            Subject     = "quote.created"
        };

        await _sender.SendMessageAsync(message, ct);
    }
}
