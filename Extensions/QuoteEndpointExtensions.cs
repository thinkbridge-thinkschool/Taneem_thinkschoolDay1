using Microsoft.EntityFrameworkCore;
using QuotesApi.BackgroundJobs;
using QuotesApi.Commands;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Queries;
using QuotesApi.Repositories;
using System.Text.Json;

namespace QuotesApi.Extensions;

public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        // ── GET /api/quotes ───────────────────────────────────────────────
        group.MapGet("/", async (
            int page,
            int size,
            IQuoteRepository repo,
            CancellationToken ct) =>
        {
            page = page <= 0 ? 1 : page;
            size = size <= 0 ? 10 : size;

            var quotes = await repo.GetPagedAsync(page, size, ct);
            return Results.Ok(quotes.Select(ToResponse));
        });

        // ── GET /api/quotes/{id} ──────────────────────────────────────────
        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repo,
            CancellationToken ct) =>
        {
            var quote = await repo.GetByIdAsync(id, ct);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(ToResponse(quote));
        });

        // ── POST /api/quotes ──────────────────────────────────────────────
        group.MapPost("/", async (
            CreateQuoteRequest request,
            AppDbContext       db,
            QuoteJobQueue      jobQueue,
            CancellationToken  ct) =>
        {
            var quote = Quote.Create(request.Author, request.Text);

            // Outbox pattern: save quote + outbox row in one transaction.
            // ExecuteAsync is required because EnableRetryOnFailure blocks user-initiated
            // transactions unless wrapped in the execution strategy.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                db.Quotes.Add(quote);
                await db.SaveChangesAsync(ct); // quote gets its Id here

                db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "quote.created",
                    Payload   = JsonSerializer.Serialize(new { quoteId = quote.Id, author = quote.Author }),
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });

            jobQueue.Enqueue(quote.Id);

            return Results.Created($"/api/quotes/{quote.Id}", ToResponse(quote));
        }).RequireAuthorization("can-write-quotes");

        // ── DELETE /api/quotes/{id} ───────────────────────────────────────
        // Soft delete — quote is hidden, not removed from the database.
        group.MapDelete("/{id:int}", async (
            int              id,
            IQuoteRepository repo,
            CancellationToken ct) =>
        {
            var deleted = await repo.SoftDeleteAsync(id, ct);

            return deleted
                ? Results.NoContent()
                : Results.NotFound();
        }).RequireAuthorization("can-delete-quotes");

        // ── GET /api/quotes/count ────────────────────────────────────────
        group.MapGet("/count", async (AppDbContext db, CancellationToken ct) =>
        {
            var count = await db.Quotes.CountAsync(q => !q.IsDeleted, ct);
            return Results.Ok(new { count });
        });

        // ── GET /api/quotes/author-stats ─────────────────────────────────
        // Returns all distinct authors matching the prefix with their quote counts.
        // Used to populate chips independently of pagination.
        group.MapGet("/author-stats", async (string? search, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Quotes.Where(q => !q.IsDeleted);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(q => q.Author.StartsWith(search));

            var stats = await query
                .GroupBy(q => q.Author)
                .Select(g => new { author = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            return Results.Ok(stats);
        });

        // ── GET /api/quotes/summary ── CQRS read model ───────────────────
        group.MapGet("/summary", async (
            int page,
            int size,
            string? search,
            bool? exactAuthor,
            GetQuotesSummaryHandler handler,
            CancellationToken ct) =>
        {
            page = page <= 0 ? 1 : page;
            size = size <= 0 ? 10 : size;

            var results = await handler.HandleAsync(
                new GetQuotesSummaryQuery(page, size, search, exactAuthor ?? false), ct);

            return Results.Ok(results);
        });

        // ── POST /api/quotes/command ── CQRS write model ──────────────────
        group.MapPost("/command", async (
            CreateQuoteCommand cmd,
            CreateQuoteHandler handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(cmd, ct);
            return Results.Created($"/api/quotes/{id}", new { id });
        }).RequireAuthorization("can-write-quotes");

        // ── GET /api/quotes/summary/dapper ── Dapper read path ───────────
        group.MapGet("/summary/dapper", async (
            int page,
            int size,
            GetQuotesSummaryDapperHandler handler,
            CancellationToken ct) =>
        {
            page = page <= 0 ? 1 : page;
            size = size <= 0 ? 10 : size;

            var results = await handler.HandleAsync(
                new GetQuotesSummaryQuery(page, size), ct);

            return Results.Ok(results);
        });

        // ── GET /api/quotes/fast ──────────────────────────────────────────
        // Fixed version: single query + index on Author.
        group.MapGet("/fast", async (AppDbContext db, CancellationToken ct) =>
        {
            var result = await db.Quotes
                .Where(q => !q.IsDeleted)
                .GroupBy(q => q.Author)
                .Select(g => new { Author = g.Key, Quotes = g.Select(q => new { q.Id, q.Text }) })
                .ToListAsync(ct);

            return Results.Ok(result);
        });

    
        // ── GET /api/quotes/slow ──────────────────────────────────────────
        // Deliberately bad: N+1 queries + no index on Author column.
        // Used for Day 11 profiling exercise only — not for production use.
        group.MapGet("/slow", async (AppDbContext db, CancellationToken ct) =>
        {
            // Query 1: fetch all distinct authors
            var authors = await db.Quotes
                .Where(q => !q.IsDeleted)
                .Select(q => q.Author)
                .Distinct()
                .ToListAsync(ct);

            // Query 2..N: one extra query per author — classic N+1
            var result = new List<object>();
            foreach (var author in authors)
            {
                var quotes = await db.Quotes
                    .Where(q => q.Author == author && !q.IsDeleted)
                    .ToListAsync(ct);

                result.Add(new { author, quotes = quotes.Select(q => new { q.Id, q.Text }) });
            }

            return Results.Ok(result);
        });

        return app;
    }

    // ── Mapping helper ────────────────────────────────────────────────────
    // IsDeleted is intentionally excluded from the response —
    // soft-deleted quotes are filtered at the repository level.

    private static QuoteResponse ToResponse(Quote q) =>
        new(q.Id, q.Author, q.Text);
}