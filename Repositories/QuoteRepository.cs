using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(
        AppDbContext db,
        ILogger<QuoteRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<Quote>> GetPagedAsync(
        int page,
        int size,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Fetching quotes page {Page} size {Size}",
            page,
            size);

        return await _db.Quotes
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Fetching quote with id {Id}",
            id);

        return await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task<Quote> CreateAsync(
        Quote quote,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Creating quote by {Author}",
            quote.Author);

        _db.Quotes.Add(quote);

        await _db.SaveChangesAsync(ct);

        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken ct)
    {
        var quote = await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        if (quote is null)
        {
            return false;
        }

        _logger.LogInformation(
            "Deleting quote with id {Id}",
            id);

        _db.Quotes.Remove(quote);

        await _db.SaveChangesAsync(ct);

        return true;
    }
}