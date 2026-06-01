using FluentAssertions;
using QuotesApi.Models;
using QuotesApi.Services;
using Xunit;

namespace Tests.Domain;

// ── Fake clock — no DI, no infrastructure, just a fixed time ─────────────────

file sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; } =
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

// ═════════════════════════════════════════════════════════════════════════════
// Collection invariant tests
// Pure domain tests — no DbContext, no fixtures, no setup methods.
// Target: < 50ms total for the suite.
// ═════════════════════════════════════════════════════════════════════════════

public sealed class CollectionInvariantTests
{
    private static readonly IClock Clock = new FakeClock();

    // ── Name invariants ───────────────────────────────────────────────────

    [Fact]
    public void Empty_name_throws()
    {
        var act = () => Collection.Create("", ownerId: 1);

        act.Should().Throw<CollectionNameInvalidException>()
           .WithMessage("*empty*");
    }

    [Fact]
    public void Whitespace_only_name_throws()
    {
        var act = () => Collection.Create("   ", ownerId: 1);

        act.Should().Throw<CollectionNameInvalidException>()
           .WithMessage("*empty*");
    }

    [Fact]
    public void Name_longer_than_80_chars_throws()
    {
        var longName = new string('x', 81);

        var act = () => Collection.Create(longName, ownerId: 1);

        act.Should().Throw<CollectionNameInvalidException>()
           .WithMessage("*80*");
    }

    [Fact]
    public void Name_shorter_than_3_chars_throws()
    {
        var act = () => Collection.Create("ab", ownerId: 1);

        act.Should().Throw<CollectionNameInvalidException>()
           .WithMessage("*3*");
    }

    [Fact]
    public void Valid_name_at_boundary_3_chars_succeeds()
    {
        var act = () => Collection.Create("abc", ownerId: 1);

        act.Should().NotThrow();
    }

    [Fact]
    public void Valid_name_at_boundary_80_chars_succeeds()
    {
        var name = new string('x', 80);

        var act = () => Collection.Create(name, ownerId: 1);

        act.Should().NotThrow();
    }

    // ── Item cap invariant ────────────────────────────────────────────────

    [Fact]
    public void Adding_51st_item_throws()
    {
        var collection = Collection.Create("Test Collection", ownerId: 1);

        // Add 50 items — all should succeed
        for (var i = 1; i <= 50; i++)
            collection.AddItem(i, Clock);

        // 51st item must throw
        var act = () => collection.AddItem(51, Clock);

        act.Should().Throw<CollectionFullException>()
           .WithMessage("*50*");
    }

    [Fact]
    public void Adding_exactly_50_items_succeeds()
    {
        var collection = Collection.Create("Test Collection", ownerId: 1);

        var act = () =>
        {
            for (var i = 1; i <= 50; i++)
                collection.AddItem(i, Clock);
        };

        act.Should().NotThrow();
        collection.Items.Should().HaveCount(50);
    }

    // ── Duplicate invariant ───────────────────────────────────────────────

    [Fact]
    public void Duplicate_quote_id_throws()
    {
        var collection = Collection.Create("Test Collection", ownerId: 1);
        collection.AddItem(quoteId: 42, Clock);

        var act = () => collection.AddItem(quoteId: 42, Clock);

        act.Should().Throw<DuplicateQuoteException>()
           .WithMessage("*42*");
    }

    // ── Remove invariant ──────────────────────────────────────────────────

    [Fact]
    public void Removing_non_existent_item_throws()
    {
        var collection = Collection.Create("Test Collection", ownerId: 1);

        var act = () => collection.RemoveItem(quoteId: 99);

        act.Should().Throw<QuoteNotInCollectionException>()
           .WithMessage("*99*");
    }

    // ── Add then remove ───────────────────────────────────────────────────

    [Fact]
    public void Adding_then_removing_leaves_zero_items()
    {
        var collection = Collection.Create("Test Collection", ownerId: 1);

        collection.AddItem(quoteId: 1, Clock);
        collection.RemoveItem(quoteId: 1);

        collection.Items.Should().BeEmpty();
    }

    [Fact]
    public void Adding_then_removing_one_of_many_leaves_correct_count()
    {
        var collection = Collection.Create("Test Collection", ownerId: 1);

        collection.AddItem(quoteId: 1, Clock);
        collection.AddItem(quoteId: 2, Clock);
        collection.AddItem(quoteId: 3, Clock);

        collection.RemoveItem(quoteId: 2);

        collection.Items.Should().HaveCount(2);
        collection.Items.Should().NotContain(i => i.QuoteId == 2);
    }
}