using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace QuotesApi.ServiceBus;

// Deliberately always throws — after max delivery count (default 10),
// Service Bus moves the message to the Dead Letter Queue automatically.
public class PoisonMessageDemo : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly ILogger<PoisonMessageDemo> _logger;

    public PoisonMessageDemo(
        ServiceBusClient client,
        IConfiguration config,
        ILogger<PoisonMessageDemo> logger)
    {
        // Reuse analytics-sub to receive the poison message
        _processor = client.CreateProcessor(
            config["ServiceBus:TopicName"]!,
            config["ServiceBus:AnalyticsSubscription"]!);

        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync   += OnErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => Task.CompletedTask);
        await _processor.StopProcessingAsync();
    }

    private Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        _logger.LogWarning("[poison-demo] Delivery {Count} — always throwing to trigger DLQ.",
            args.Message.DeliveryCount);

        // Intentionally throw — Service Bus will retry up to MaxDeliveryCount,
        // then dead-letter the message automatically.
        throw new InvalidOperationException("Simulated poison message failure.");
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "[poison-demo] Error on {EntityPath}.", args.EntityPath);
        return Task.CompletedTask;
    }
}
