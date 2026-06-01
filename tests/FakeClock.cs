using QuotesApi.Services;

namespace QuotesApi.Tests;

/// <summary>
/// A clock whose time is fixed — it never moves unless you set UtcNow manually.
/// Use this in tests instead of the real SystemClock.
/// </summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } 
        = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}