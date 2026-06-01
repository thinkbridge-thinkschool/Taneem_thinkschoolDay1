namespace QuotesApi.DTOs;

public record CreateCollectionRequest
{
    public string Name    { get; init; } = string.Empty;
    public int    OwnerId { get; init; }
}

public record AddQuoteRequest
{
    public int QuoteId { get; init; }
}

public record CollectionItemResponse(
    int            QuoteId,
    DateTimeOffset AddedAt
);

public record CollectionResponse(
    int                              Id,
    string                           Name,
    int                              OwnerId,
    IReadOnlyList<CollectionItemResponse> Items
);