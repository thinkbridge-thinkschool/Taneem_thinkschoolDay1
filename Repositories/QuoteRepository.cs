using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<List<Quote>> GetPagedAsync(int page, int size, CancellationToken ct);
    Task<Quote?> GetByIdAsync(int id, CancellationToken ct);
    Task<Quote> CreateAsync(Quote quote, CancellationToken ct);
    Task<bool> SoftDeleteAsync(int id, CancellationToken ct);
}

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(AppDbContext db, ILogger<QuoteRepository> logger)
    {
        _db    = db;
        _logger = logger;
    }

    // Only returns non-deleted quotes — soft-deleted are invisible to callers
    public async Task<List<Quote>> GetPagedAsync(
        int page, int size, CancellationToken ct)
    {
        _logger.LogInformation("Fetching quotes page {Page} size {Size}", page, size);

        return await _db.Quotes
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
    }

    public async Task<Quote?> GetByIdAsync(int id, CancellationToken ct)
    {
        _logger.LogInformation("Fetching quote with id {Id}", id);

        return await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, ct);  // ← filter soft-deleted
    }

    public async Task<Quote> CreateAsync(Quote quote, CancellationToken ct)
    {
        _logger.LogInformation("Creating quote by {Author}", quote.Author);

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(ct);
        return quote;
    }

    /// <summary>
    /// Soft delete — calls quote.SoftDelete() through the aggregate root,
    /// never sets IsDeleted directly.
    /// </summary>
    public async Task<bool> SoftDeleteAsync(int id, CancellationToken ct)
    {
        var quote = await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, ct);

        if (quote is null)
            return false;

        _logger.LogInformation("Soft-deleting quote with id {Id}", id);

        quote.SoftDelete();     // ← mutation through the aggregate, not _db.Quotes directly

        await _db.SaveChangesAsync(ct);
        return true;
    }
}