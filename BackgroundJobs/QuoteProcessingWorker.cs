namespace QuotesApi.BackgroundJobs;

// BackgroundService runs for the lifetime of the app on its own thread.
// ExecuteAsync is called once on startup and must return when stoppingToken fires.
public class QuoteProcessingWorker : BackgroundService
{
    private readonly QuoteJobQueue _queue;
    private readonly ILogger<QuoteProcessingWorker> _logger;

    public QuoteProcessingWorker(QuoteJobQueue queue, ILogger<QuoteProcessingWorker> logger)
    {
        _queue  = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quote processing worker started.");

        // ReadAllAsync yields each item as it arrives and exits cleanly
        // when stoppingToken is cancelled — this is the graceful shutdown hook.
        await foreach (var quoteId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(quoteId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log and continue — one failed job must not kill the whole worker.
                _logger.LogError(ex, "Failed to process quote {QuoteId}.", quoteId);
            }
        }

        _logger.LogInformation("Quote processing worker stopped.");
    }

    private async Task ProcessAsync(int quoteId, CancellationToken ct)
    {
        _logger.LogInformation("Processing quote {QuoteId}…", quoteId);

        // Simulate slow off-thread work: sending an email, updating a search
        // index, publishing an event — anything you don't want on the request thread.
        await Task.Delay(2000, ct);

        _logger.LogInformation("Quote {QuoteId} processed.", quoteId);
    }
}
