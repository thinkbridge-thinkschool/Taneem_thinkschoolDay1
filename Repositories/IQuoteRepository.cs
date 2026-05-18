using QuotesApi.Models;
public interface IQuoteRepository
{
    Task<List<Quote>> GetPagedAsync(int page, int size, CancellationToken ct);

    Task<Quote?> GetByIdAsync(int id, CancellationToken ct);

    Task<Quote> CreateAsync(Quote quote, CancellationToken ct);

    Task<bool> DeleteAsync(int id, CancellationToken ct);
}