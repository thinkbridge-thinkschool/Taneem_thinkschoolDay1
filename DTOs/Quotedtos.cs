using System.ComponentModel.DataAnnotations;

namespace QuotesApi.DTOs;

public record CreateQuoteRequest(
    [Required] string Author,
    [Required] string Text
);

/// <summary>
/// Response DTO — IsDeleted is deliberately excluded.
/// Soft-deleted quotes are filtered at the repository level
/// and never reach the caller.
/// </summary>
public record QuoteResponse(
    int    Id,
    string Author,
    string Text
);

// Detail response — includes canDelete computed server-side from ownership/role.
// Frontend never decides who can delete; it just reads this flag.
public record QuoteDetailResponse(
    int    Id,
    string Author,
    string Text,
    bool   CanDelete
);