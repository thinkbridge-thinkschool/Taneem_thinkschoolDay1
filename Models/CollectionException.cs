namespace QuotesApi.Models;

/// <summary>Base type so the middleware can catch all collection domain errors in one place.</summary>
public abstract class CollectionDomainException : Exception
{
    protected CollectionDomainException(string message) : base(message) { }
}

public sealed class CollectionNotFoundException : CollectionDomainException
{
    public CollectionNotFoundException(int id)
        : base($"Collection {id} was not found.") { }
}

public sealed class CollectionNameInvalidException : CollectionDomainException
{
    public CollectionNameInvalidException(string message)
        : base(message) { }
}

public sealed class CollectionFullException : CollectionDomainException
{
    public CollectionFullException(int id)
        : base($"Collection {id} already contains 50 items (the maximum).") { }
}

public sealed class DuplicateQuoteException : CollectionDomainException
{
    public DuplicateQuoteException(int collectionId, int quoteId)
        : base($"Quote {quoteId} is already in collection {collectionId}.") { }
}

public sealed class QuoteNotInCollectionException : CollectionDomainException
{
    public QuoteNotInCollectionException(int collectionId, int quoteId)
        : base($"Quote {quoteId} is not in collection {collectionId}.") { }
}