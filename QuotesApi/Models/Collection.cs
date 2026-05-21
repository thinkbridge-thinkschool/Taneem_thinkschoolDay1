namespace QuotesApi.Models;

/// <summary>
/// Aggregate root. All mutation goes through public methods —
/// nothing outside this class touches Items directly.
/// </summary>
public sealed class Collection
{
    // ── Persistence fields (EF needs a private setter or backing field) ────

    private readonly List<CollectionItem> _items = [];

    public int    Id      { get; private set; }
    public string Name    { get; private set; } = string.Empty;
    public int    OwnerId { get; private set; }

    /// <summary>Read-only view — callers cannot mutate the list directly.</summary>
    public IReadOnlyList<CollectionItem> Items => _items.AsReadOnly();

    // ── Required by EF Core (parameterless constructor) ───────────────────

    private Collection() { }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new Collection, enforcing the Name invariant immediately.
    /// </summary>
    public static Collection Create(string name, int ownerId)
    {
        var collection = new Collection { OwnerId = ownerId };
        collection.SetName(name);   // invariant checked here
        return collection;
    }

    // ── Invariant-enforcing methods ───────────────────────────────────────

    /// <summary>Renames the collection. Enforces 3-80 char invariant.</summary>
    public void Rename(string name) => SetName(name);

    /// <summary>
    /// Adds a quote to the collection.
    /// Throws if the collection is full or the quote is already present.
    /// </summary>
    public void AddItem(int quoteId)
    {
        if (_items.Count >= 50)
            throw new CollectionFullException(Id);

        if (_items.Any(i => i.QuoteId == quoteId))
            throw new DuplicateQuoteException(Id, quoteId);

        _items.Add(CollectionItem.Create(quoteId));
    }

    /// <summary>
    /// Removes a quote from the collection.
    /// Throws if the quote is not present.
    /// </summary>
    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId)
            ?? throw new QuoteNotInCollectionException(Id, quoteId);

        _items.Remove(item);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new CollectionNameInvalidException(
                "Collection name cannot be empty.");

        var trimmed = name.Trim();

        if (trimmed.Length < 3 || trimmed.Length > 80)
            throw new CollectionNameInvalidException(
                $"Collection name must be between 3 and 80 characters (got {trimmed.Length}).");

        Name = trimmed;
    }
}

/// <summary>
/// Value object — immutable after creation.
/// EF Core maps this as an owned type inside Collection.
/// </summary>
public sealed class CollectionItem
{
    public int            QuoteId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    private CollectionItem() { }   // required by EF

    internal static CollectionItem Create(int quoteId) => new()
    {
        QuoteId = quoteId,
        AddedAt = DateTimeOffset.UtcNow
    };
}