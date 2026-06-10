using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.ServiceBus;

namespace QuotesApi.BackgroundJobs;

public class OutboxRelay : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelay> _logger;

    public OutboxRelay(IServiceScopeFactory scopeFactory, ILogger<OutboxRelay> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxRelay started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessBatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => Task.CompletedTask);
        }

        _logger.LogInformation("OutboxRelay stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope     = _scopeFactory.CreateAsyncScope();
        var db                    = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher             = scope.ServiceProvider.GetRequiredService<QuoteCreatedPublisher>();

        var unsent = await db.OutboxMessages
            .Where(m => m.SentAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        if (unsent.Count == 0) return;

        _logger.LogInformation("OutboxRelay processing {Count} unsent message(s).", unsent.Count);

        foreach (var msg in unsent)
        {
            await publisher.PublishRawAsync(msg.EventType, msg.Payload, msg.Id.ToString(), ct);
            msg.SentAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("OutboxRelay sent message {Id} ({EventType}).", msg.Id, msg.EventType);
        }
    }
}
