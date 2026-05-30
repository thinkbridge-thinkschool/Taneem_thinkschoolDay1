namespace QuotesApi.Queries;

// Day 12 — CQRS-lite: read model (projection shaped for the list screen)
// Different from the write model (Quote entity) — contains derived fields
// the screen needs but the domain does not care about.
// AuthorInitials and ShortText do not exist on the Quote entity.
public record QuoteSummaryReadModel(
    int    Id,
    string Author,
    string AuthorInitials, // e.g. "Marcus Aurelius" → "MA"
    string ShortText       // first 100 chars for list view
);
