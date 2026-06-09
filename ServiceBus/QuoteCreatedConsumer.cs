using Azure.Messaging.ServiceBus;
using System.Collections.Concurrent;
using System.Text.Json;

namespace QuotesApi.ServiceBus;

public class QuoteCreatedConsumer : BackgroundService
{
    private readonly ServiceBusProcessor _emailProcessor;
    private readonly ServiceBusProcessor _analyticsProcessor;
    private readonly ILogger<QuoteCreatedConsumer> _logger;

    // In-memory set of already-processed MessageIds — idempotency guard.
    // In production this would be a DB/Redis check.
    private readonly ConcurrentDictionary<string, bool> _processed = new();

    public QuoteCreatedConsumer(
        ServiceBusClient client,
        IConfiguration config,
        ILogger<QuoteCreatedConsumer> logger)
    {
        var topic         = config["ServiceBus:TopicName"]!;
        var emailSub      = config["ServiceBus:EmailSubscription"]!;
        var analyticsSub  = config["ServiceBus:AnalyticsSubscription"]!;

        _emailProcessor     = client.CreateProcessor(topic, emailSub);
        _analyticsProcessor = client.CreateProcessor(topic, analyticsSub);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wire up handlers for both subscriptions
        _emailProcessor.ProcessMessageAsync     += OnEmailMessageAsync;
        _emailProcessor.ProcessErrorAsync       += OnErrorAsync;
        _analyticsProcessor.ProcessMessageAsync += OnAnalyticsMessageAsync;
        _analyticsProcessor.ProcessErrorAsync   += OnErrorAsync;

        await _emailProcessor.StartProcessingAsync(stoppingToken);
        await _analyticsProcessor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("Service Bus consumers started.");

        // Keep running until the app shuts down
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => Task.CompletedTask);

        // Graceful shutdown — stop both processors
        await _emailProcessor.StopProcessingAsync();
        await _analyticsProcessor.StopProcessingAsync();

        _logger.LogInformation("Service Bus consumers stopped.");
    }

    private async Task OnEmailMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = $"email:{args.Message.MessageId}";

        // Idempotency check — skip if already processed
        if (!_processed.TryAdd(messageId, true))
        {
            _logger.LogWarning("Duplicate message {MessageId} skipped (email-sub).", messageId);
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        var body = args.Message.Body.ToString();
        var data = JsonSerializer.Deserialize<QuoteCreatedPayload>(body);

        _logger.LogInformation("[email-sub] Quote {QuoteId} by {Author} — sending email…", data?.QuoteId, data?.Author);

        // Simulate sending email
        await Task.Delay(500, args.CancellationToken);

        _logger.LogInformation("[email-sub] Email sent for quote {QuoteId}.", data?.QuoteId);

        await args.CompleteMessageAsync(args.Message);
    }

    private async Task OnAnalyticsMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = $"analytics:{args.Message.MessageId}";

        // Idempotency check
        if (!_processed.TryAdd(messageId, true))
        {
            _logger.LogWarning("Duplicate message {MessageId} skipped (analytics-sub).", messageId);
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        var body = args.Message.Body.ToString();
        var data = JsonSerializer.Deserialize<QuoteCreatedPayload>(body);

        _logger.LogInformation("[analytics-sub] Quote {QuoteId} by {Author} — logging analytics…", data?.QuoteId, data?.Author);

        // Simulate analytics update
        await Task.Delay(200, args.CancellationToken);

        _logger.LogInformation("[analytics-sub] Analytics logged for quote {QuoteId}.", data?.QuoteId);

        await args.CompleteMessageAsync(args.Message);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus error on {EntityPath}.", args.EntityPath);
        return Task.CompletedTask;
    }

    private record QuoteCreatedPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("quoteId")] int QuoteId,
        [property: System.Text.Json.Serialization.JsonPropertyName("author")]  string Author);
}
