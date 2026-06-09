using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace QuotesApi.ServiceBus;

public class QuoteCreatedPublisher
{
    private readonly ServiceBusSender _sender;

    public QuoteCreatedPublisher(ServiceBusClient client, IConfiguration config)
    {
        _sender = client.CreateSender(config["ServiceBus:TopicName"]);
    }

    public async Task PublishAsync(int quoteId, string author, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { quoteId, author });

        var message = new ServiceBusMessage(payload)
        {
            MessageId     = $"quote-created-{quoteId}", // used for idempotency
            ContentType   = "application/json",
            Subject       = "quote.created"
        };

        await _sender.SendMessageAsync(message, ct);
    }
}
