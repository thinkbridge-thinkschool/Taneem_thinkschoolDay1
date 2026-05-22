using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Repositories;

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
        // Validation is now in the aggregate — the endpoint just calls Create.
        // QuoteDomainException bubbles up to the exception middleware.
        group.MapPost("/", async (
            CreateQuoteRequest request,
            IQuoteRepository   repo,
            CancellationToken  ct) =>
        {
            // Quote.Create enforces all invariants — no manual validation here.
            // If author or text are invalid, QuoteDomainException is thrown
            // and caught by the middleware which returns 422.
            var quote   = Quote.Create(request.Author, request.Text);
            var created = await repo.CreateAsync(quote, ct);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                ToResponse(created));
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

        return app;
    }

    // ── Mapping helper ────────────────────────────────────────────────────
    // IsDeleted is intentionally excluded from the response —
    // soft-deleted quotes are filtered at the repository level.

    private static QuoteResponse ToResponse(Quote q) =>
        new(q.Id, q.Author, q.Text);
}