using QuotesApi.Models;
using QuotesApi.Tests;
using Xunit;

namespace QuotesApi.Tests;

public sealed class ClockTests
{
    [Fact]
    public void AddItem_sets_AddedAt_to_clock_time()
    {
        // ── Arrange ───────────────────────────────────────────────────────
        
        // Fix the clock to a known time
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        // Create a collection
        var collection = Collection.Create("My Test Collection", ownerId: 1);

        // ── Act ───────────────────────────────────────────────────────────

        // Add a quote — passes the fixed clock in
        collection.AddItem(quoteId: 5, clock);

        // ── Assert ────────────────────────────────────────────────────────

        // The item's AddedAt must exactly match what the clock returned
        var item = collection.Items.Single();

        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            item.AddedAt);
    }

    [Fact]
    public void AddItem_uses_clock_time_not_system_time()
    {
        // ── Arrange ───────────────────────────────────────────────────────

        // Set clock to a time in the past — clearly not "now"
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2000, 6, 15, 12, 30, 0, TimeSpan.Zero)
        };

        var collection = Collection.Create("Another Collection", ownerId: 2);

        // ── Act ───────────────────────────────────────────────────────────

        collection.AddItem(quoteId: 10, clock);

        // ── Assert ────────────────────────────────────────────────────────

        // AddedAt must be year 2000 — proving it used the clock, not DateTime.UtcNow
        Assert.Equal(2000, collection.Items.Single().AddedAt.Year);
        Assert.Equal(6,    collection.Items.Single().AddedAt.Month);
        Assert.Equal(15,   collection.Items.Single().AddedAt.Day);
    }
}