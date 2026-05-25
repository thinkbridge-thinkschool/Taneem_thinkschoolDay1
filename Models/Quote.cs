namespace QuotesApi.Models;

/// <summary>
/// Rich entity. Construction goes through Quote.Create() — never new Quote {}.
/// Text is immutable after creation. Deletion is soft-only via IsDeleted flag.
/// </summary>
public sealed class Quote
{
    // ── Persistence backing fields ─────────────────────────────────────────

    public int    Id        { get; private set; }
    public string Author    { get; private set; } = string.Empty;
    public string Text      { get; private set; } = string.Empty;
    public bool   IsDeleted { get; private set; }
    public int CreatedByUserId { get; private set; }

    // ── EF Core requires a parameterless constructor ───────────────────────

    private Quote() { }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// The only way to create a Quote. Enforces all invariants at construction.
    /// Throws QuoteDomainException if author or text violate the rules.
    /// </summary>
    public static Quote Create(string author, string text, int createdByUserId = 0)
{
    ValidateAuthor(author);
    ValidateText(text);
    return new Quote
    {
        Author          = author.Trim(),
        Text            = text.Trim(),
        CreatedByUserId = createdByUserId
    };
}

    // ── Mutation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Soft-deletes the quote. Text and Author are preserved for audit purposes.
    /// Hard delete is not supported — once a quote exists, it is only hidden.
    /// </summary>
    public void SoftDelete()
    {
        IsDeleted = true;
    }

    // Text is intentionally not mutable after creation.
    // If you need to "fix" a quote, soft-delete it and create a new one.

    // ── Private validation ────────────────────────────────────────────────

    private static void ValidateAuthor(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
            throw new QuoteAuthorInvalidException("Author cannot be empty.");

        var trimmed = author.Trim();
        if (trimmed.Length > 200)
            throw new QuoteAuthorInvalidException(
                $"Author must be 200 characters or fewer (got {trimmed.Length}).");
    }

    private static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new QuoteTextInvalidException("Quote text cannot be empty.");

        var trimmed = text.Trim();
        if (trimmed.Length > 1000)
            throw new QuoteTextInvalidException(
                $"Quote text must be 1000 characters or fewer (got {trimmed.Length}).");
    }
}