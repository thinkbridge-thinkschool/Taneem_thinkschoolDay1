using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace QuotesApi.Queries;

// Day 12 — Dapper implementation of the same summary query
// Raw SQL — no ORM overhead, no change tracking, no expression tree compilation.
// Use on hot read paths where EF's overhead is measurable.
public class GetQuotesSummaryDapperHandler
{
    private readonly string _connectionString;

    public GetQuotesSummaryDapperHandler(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Default")!;
    }

    public async Task<IEnumerable<QuoteSummaryReadModel>> HandleAsync(
        GetQuotesSummaryQuery query, CancellationToken ct)
    {
        // Raw SQL — exactly what hits the database, no translation layer
        const string sql = """
            SELECT Id, Author, Text
            FROM Quotes
            WHERE IsDeleted = 0
            ORDER BY Id
            OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY
            """;

        using var connection = new SqlConnection(_connectionString);

        var rows = await connection.QueryAsync<(int Id, string Author, string Text)>(
            sql,
            new { Offset = (query.Page - 1) * query.Size, query.Size });

        // Same client-side projection as the EF version
        return rows.Select(q => new QuoteSummaryReadModel(
            q.Id,
            q.Author,
            string.Concat(q.Author.Split(' ').Where(w => w.Length > 0).Select(w => w[0])),
            q.Text.Length > 100 ? q.Text[..100] + "…" : q.Text
        ));
    }
}
