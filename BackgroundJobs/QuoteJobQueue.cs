using System.Threading.Channels;

namespace QuotesApi.BackgroundJobs;

// Thread-safe in-memory queue backed by System.Threading.Channels.
// The API writes quoteIds in; the worker reads them out.
// Unbounded = never blocks the writer — safe for fire-and-forget from endpoints.
public class QuoteJobQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    public void Enqueue(int quoteId) => _channel.Writer.TryWrite(quoteId);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
