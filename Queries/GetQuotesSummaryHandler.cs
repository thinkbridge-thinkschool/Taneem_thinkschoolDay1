using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

public class GetQuotesSummaryHandler
{
    private readonly AppDbContext _db;

    public GetQuotesSummaryHandler(AppDbContext db) => _db = db;

    public async Task<List<QuoteSummaryReadModel>> HandleAsync(
        GetQuotesSummaryQuery query, CancellationToken ct)
    {
        var baseQuery = _db.Quotes.Where(q => !q.IsDeleted);
        if (query.CurrentUserId.HasValue)
            baseQuery = baseQuery.Where(q => q.CreatedByUserId == query.CurrentUserId.Value);

        // ── Chip click: all quotes by exact author, paginated ──────────────
        if (!string.IsNullOrWhiteSpace(query.Search) && query.ExactAuthor)
        {
            var total = await baseQuery
                .CountAsync(q => q.Author == query.Search, ct);

            var exactRows = await baseQuery
                .Where(q => q.Author == query.Search)
                .OrderBy(q => q.Id)
                .Skip((query.Page - 1) * query.Size)
                .Take(query.Size)
                .Select(q => new { q.Id, q.Author, q.Text })
                .ToListAsync(ct);

            return exactRows.Select(q => new QuoteSummaryReadModel(
                q.Id,
                q.Author,
                string.Concat(q.Author.Split(' ').Select(w => w[0])),
                q.Text.Length > 100 ? q.Text[..100] + "…" : q.Text,
                total
            )).ToList();
        }

        // ── Typing search: ALL matching quotes, ordered by author then id ──
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchRows = await baseQuery
                .Where(q => q.Author.StartsWith(query.Search))
                .OrderBy(q => q.Author)
                .ThenBy(q => q.Id)
                .Skip((query.Page - 1) * query.Size)
                .Take(query.Size)
                .Select(q => new {
                    q.Id,
                    q.Author,
                    q.Text,
                    QuoteCount = _db.Quotes.Count(q2 => !q2.IsDeleted && q2.Author == q.Author)
                })
                .ToListAsync(ct);

            return searchRows.Select(q => new QuoteSummaryReadModel(
                q.Id,
                q.Author,
                string.Concat(q.Author.Split(' ').Select(w => w[0])),
                q.Text.Length > 100 ? q.Text[..100] + "…" : q.Text,
                q.QuoteCount
            )).ToList();
        }

        // ── Browse mode ────────────────────────────────────────────────────
        var rows = await baseQuery
            .OrderBy(q => q.Id)
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size)
            .Select(q => new { q.Id, q.Author, q.Text })
            .ToListAsync(ct);

        return rows.Select(q => new QuoteSummaryReadModel(
            q.Id,
            q.Author,
            string.Concat(q.Author.Split(' ').Select(w => w[0])),
            q.Text.Length > 100 ? q.Text[..100] + "…" : q.Text
        )).ToList();
    }
}
