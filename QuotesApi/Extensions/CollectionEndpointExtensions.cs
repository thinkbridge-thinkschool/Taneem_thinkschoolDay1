using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class CollectionEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections");

        // ── POST /api/collections ─────────────────────────────────────────
        // Creates a new collection. Name invariant enforced by the aggregate.

        group.MapPost("/", async (
            CreateCollectionRequest request,
            ICollectionRepository   repo,
            CancellationToken       ct) =>
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Name))
                errors["name"] = ["Name is required."];

            if (request.OwnerId <= 0)
                errors["ownerId"] = ["OwnerId must be a positive integer."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // Invariants (length, whitespace) enforced inside Collection.Create.
            // CollectionNameInvalidException bubbles up to ExceptionMiddleware.
            var collection = Collection.Create(request.Name, request.OwnerId);
            var created    = await repo.AddAsync(collection, ct);

            return Results.Created(
                $"/api/collections/{created.Id}",
                ToResponse(created));
        });

        // ── POST /api/collections/{id}/items ──────────────────────────────
        // Adds a quote to the collection through the aggregate root.
        // Throws CollectionFullException or DuplicateQuoteException if invariants break.

        group.MapPost("/{id:int}/items", async (
            int               id,
            AddQuoteRequest   request,
            ICollectionRepository repo,
            CancellationToken ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);

            if (collection is null)
                return Results.NotFound();

            // All invariant checks happen inside AddItem — not here, not in the repo.
            collection.AddItem(request.QuoteId);

            await repo.UpdateAsync(collection, ct);

            return Results.Ok(ToResponse(collection));
        });

        // ── DELETE /api/collections/{id}/items/{quoteId} ──────────────────
        // Removes a quote from the collection through the aggregate root.
        // Throws QuoteNotInCollectionException if the quote isn't present.

        group.MapDelete("/{id:int}/items/{quoteId:int}", async (
            int                   id,
            int                   quoteId,
            ICollectionRepository repo,
            CancellationToken     ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);

            if (collection is null)
                return Results.NotFound();

            collection.RemoveItem(quoteId);

            await repo.UpdateAsync(collection, ct);

            return Results.Ok(ToResponse(collection));
        });

        return app;
    }

    // ── Mapping helper ────────────────────────────────────────────────────

    private static CollectionResponse ToResponse(Collection c) => new(
        c.Id,
        c.Name,
        c.OwnerId,
        c.Items
         .Select(i => new CollectionItemResponse(i.QuoteId, i.AddedAt))
         .ToList());
}