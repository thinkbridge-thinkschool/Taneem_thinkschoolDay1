namespace QuotesApi.Models;

public abstract class QuoteDomainException : Exception
{
    protected QuoteDomainException(string message) : base(message) { }
}

public sealed class QuoteAuthorInvalidException : QuoteDomainException
{
    public QuoteAuthorInvalidException(string message) : base(message) { }
}

public sealed class QuoteTextInvalidException : QuoteDomainException
{
    public QuoteTextInvalidException(string message) : base(message) { }
}

public sealed class QuoteNotFoundException : QuoteDomainException
{
    public QuoteNotFoundException(int id)
        : base($"Quote {id} was not found.") { }
}