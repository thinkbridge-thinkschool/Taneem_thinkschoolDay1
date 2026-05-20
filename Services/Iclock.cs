namespace QuotesApi.Services;

/// <summary>
/// Abstracts the system clock so tests can fix time to a known value.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Production implementation — just wraps DateTimeOffset.UtcNow.
/// Registered as singleton: one instance, zero state, safe to share.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}