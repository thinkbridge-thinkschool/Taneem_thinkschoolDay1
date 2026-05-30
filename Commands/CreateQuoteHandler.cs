using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Commands;

// Day 12 — CQRS-lite: command handler (write side)
// Single responsibility: validate + persist. Returns only the new Id.
// Has no knowledge of read models or how the UI displays quotes.
public class CreateQuoteHandler
{
    private readonly AppDbContext _db;

    public CreateQuoteHandler(AppDbContext db) => _db = db;

    public async Task<int> HandleAsync(CreateQuoteCommand cmd, CancellationToken ct)
    {
        // Validation happens inside Quote.Create() — handler stays thin
        var quote = Quote.Create(cmd.Author, cmd.Text);

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(ct);

        // Returns only the Id — caller does not need the full entity
        return quote.Id;
    }
}
