using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

// Day 12 — CQRS-lite: query handler (read side)
// Fetches only the columns the screen needs — no full entity loading.
// Projects directly to the read model. Never touches the write path.
public class GetQuotesSummaryHandler
{
    private readonly AppDbContext _db;

    public GetQuotesSummaryHandler(AppDbContext db) => _db = db;

    public async Task<List<QuoteSummaryReadModel>> HandleAsync(
        GetQuotesSummaryQuery query, CancellationToken ct)
    {
        // Fetch only Id, Author, Text from DB — IsDeleted and CreatedByUserId
        // are write-side concerns and are not needed for the list screen
        var rows = await _db.Quotes
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id)
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size)
            .Select(q => new { q.Id, q.Author, q.Text })
            .ToListAsync(ct);

        // AuthorInitials and ShortText cannot be translated to SQL —
        // computed client-side after the minimal DB fetch
        return rows.Select(q => new QuoteSummaryReadModel(
            q.Id,
            q.Author,
            string.Concat(q.Author.Split(' ').Select(w => w[0])),
            q.Text.Length > 100 ? q.Text[..100] + "…" : q.Text
        )).ToList();
    }
}
