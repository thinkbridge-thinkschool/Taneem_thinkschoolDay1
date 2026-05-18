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

        group.MapGet("/", async (
            int page,
            int size,
            IQuoteRepository repo,
            CancellationToken ct) =>
        {
            page = page <= 0 ? 1 : page;
            size = size <= 0 ? 10 : size;

            var quotes = await repo.GetPagedAsync(
                page,
                size,
                ct);

            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repo,
            CancellationToken ct) =>
        {
            var quote = await repo.GetByIdAsync(id, ct);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        group.MapPost("/", async (
            CreateQuoteRequest request,
            IQuoteRepository repo,
            CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Author))
            {
                errors["author"] =
                    ["Author is required"];
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                errors["text"] =
                    ["Text is required"];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var quote = new Quote
            {
                Author = request.Author,
                Text = request.Text
            };

            var created =
                await repo.CreateAsync(quote, ct);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repo,
            CancellationToken ct) =>
        {
            var deleted =
                await repo.DeleteAsync(id, ct);

            return deleted
                ? Results.NoContent()
                : Results.NotFound();
        });

        return app;
    }
}