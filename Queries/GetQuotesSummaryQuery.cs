namespace QuotesApi.Queries;

// Day 12 — CQRS-lite: query (read side)
// Carries only the parameters needed to retrieve the list.
// Completely separate from the write model.
public record GetQuotesSummaryQuery(int Page, int Size);
