namespace QuotesApi.Commands;

// Day 12 — CQRS-lite: write side
// The command carries only what is needed to create a quote.
// It knows nothing about how the data will be displayed.
public record CreateQuoteCommand(string Author, string Text);
