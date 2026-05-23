using QuotesApi.Models;
using QuotesApi.Services;

namespace Quotes.Tests.Unit;

// One class covers Collection + CollectionItem together because CollectionItem
// is an owned value object — it only exists through Collection.AddItem().
//
// NSubstitute replaces IClock so we can:
//   a) control exactly what timestamp is returned
//   b) prove AddedAt comes from the clock, not from DateTime.UtcNow

public sealed class CollectionTests
{
    // ── Collection.Create — success paths ────────────────────────────────

    [Fact]
    public void Create_ValidName_SetsNameAndOwnerId()
    {
        // Arrange / Act
        var collection = Collection.Create("My Favourites", ownerId: 42);

        // Assert
        collection.Name.Should().Be("My Favourites");
        collection.OwnerId.Should().Be(42);
        collection.Items.Should().BeEmpty();
    }

    [Fact]
    public void Create_NameWithLeadingAndTrailingSpaces_StoresTrimmedName()
    {
        // Arrange / Act
        var collection = Collection.Create("  Quotes  ", ownerId: 1);

        // Assert — SetName trims before storing
        collection.Name.Should().Be("Quotes");
    }

    [Theory]
    [InlineData("abc")]   // exactly 3 chars — lower boundary (inclusive)
    [InlineData("abcd")]  // 4 chars — comfortably valid
    public void Create_NameAtOrAboveMinimumLength_Succeeds(string name)
    {
        // Arrange / Act
        var act = () => Collection.Create(name, ownerId: 1);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_NameExactly80Chars_Succeeds()
    {
        // Arrange — 80 chars is the inclusive upper boundary
        var name = new string('x', 80);

        // Act
        var act = () => Collection.Create(name, ownerId: 1);

        // Assert
        act.Should().NotThrow();
    }

    // ── Collection.Create — name failure modes ────────────────────────────

    [Theory]
    [InlineData("")]      // empty string
    [InlineData("  ")]    // whitespace-only
    public void Create_BlankName_ThrowsCollectionNameInvalidException(string name)
    {
        // Arrange / Act
        var act = () => Collection.Create(name, ownerId: 1);

        // Assert
        act.Should().Throw<CollectionNameInvalidException>()
           .WithMessage("*empty*");
    }

    [Fact]
    public void Create_NameOf2Chars_ThrowsCollectionNameInvalidException()
    {
        // Arrange — one char below the minimum
        var act = () => Collection.Create("ab", ownerId: 1);

        // Assert — error must mention the 3-char minimum
        act.Should().Throw<CollectionNameInvalidException>()
           .WithMessage("*3*");
    }

    [Fact]
    public void Create_NameOf81Chars_ThrowsCollectionNameInvalidException()
    {
        // Arrange — one char above the maximum
        var name = new string('x', 81);

        // Act
        var act = () => Collection.Create(name, ownerId: 1);

        // Assert — error must mention the 80-char cap
        act.Should().Throw<CollectionNameInvalidException>()
           .WithMessage("*80*");
    }

    // ── Collection.Rename ─────────────────────────────────────────────────

    [Fact]
    public void Rename_ValidName_UpdatesName()
    {
        // Arrange
        var collection = Collection.Create("Old Name", ownerId: 1);

        // Act
        collection.Rename("New Name");

        // Assert
        collection.Name.Should().Be("New Name");
    }

    [Fact]
    public void Rename_TooShortName_ThrowsCollectionNameInvalidException()
    {
        // Arrange
        var collection = Collection.Create("Valid Name", ownerId: 1);

        // Act — same validation path as Create
        var act = () => collection.Rename("ab");

        // Assert
        act.Should().Throw<CollectionNameInvalidException>();
    }

    // ── AddItem — IClock injection (CollectionItem.AddedAt) ───────────────

    [Fact]
    public void AddItem_WithFakeClock_SetsAddedAtFromClock()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(fixedTime);   // NSubstitute: always return this value

        var collection = Collection.Create("Test", ownerId: 1);

        // Act
        collection.AddItem(quoteId: 10, clock);

        // Assert
        collection.Items.Should().ContainSingle();
        collection.Items[0].AddedAt.Should().Be(fixedTime);
    }

    [Fact]
    public void AddItem_DoesNotUseSystemClock_UsesInjectedClock()
    {
        // Arrange — deliberately use a time far in the past so this cannot
        // accidentally match DateTime.UtcNow, proving the injected clock is used
        var pastTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(pastTime);

        var collection = Collection.Create("Test", ownerId: 1);

        // Act
        collection.AddItem(quoteId: 5, clock);

        // Assert
        collection.Items[0].AddedAt.Should().Be(pastTime,
            because: "AddedAt must come from the injected IClock, not DateTime.UtcNow");
    }

    [Fact]
    public void AddItem_TwoItemsWithSequentialClockTimes_RecordsEachTimestampIndependently()
    {
        // Arrange — NSubstitute sequences: first call returns time1, second returns time2
        var time1 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(time1, time2);  // each AddItem reads UtcNow once

        var collection = Collection.Create("Test", ownerId: 1);

        // Act
        collection.AddItem(quoteId: 1, clock);
        collection.AddItem(quoteId: 2, clock);

        // Assert
        collection.Items[0].AddedAt.Should().Be(time1);
        collection.Items[1].AddedAt.Should().Be(time2);
    }

    // ── AddItem — cap and duplicate invariants (from domain rules) ────────

    [Fact]
    public void AddItem_WhenCollectionIsAlreadyFull_ThrowsCollectionFullException()
    {
        // Arrange — fill to exactly 50
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var collection = Collection.Create("Full", ownerId: 1);
        for (var i = 1; i <= 50; i++)
            collection.AddItem(i, clock);

        // Act — attempt to add the 51st item
        var act = () => collection.AddItem(quoteId: 51, clock);

        // Assert
        act.Should().Throw<CollectionFullException>()
           .WithMessage("*50*");
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_ThrowsDuplicateQuoteException()
    {
        // Arrange
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var collection = Collection.Create("Test", ownerId: 1);
        collection.AddItem(quoteId: 42, clock);

        // Act — try to add the same quote a second time
        var act = () => collection.AddItem(quoteId: 42, clock);

        // Assert
        act.Should().Throw<DuplicateQuoteException>()
           .WithMessage("*42*");
    }

    // ── RemoveItem ────────────────────────────────────────────────────────

    [Fact]
    public void RemoveItem_NonExistentQuoteId_ThrowsQuoteNotInCollectionException()
    {
        // Arrange
        var collection = Collection.Create("Test", ownerId: 1);

        // Act
        var act = () => collection.RemoveItem(quoteId: 99);

        // Assert
        act.Should().Throw<QuoteNotInCollectionException>()
           .WithMessage("*99*");
    }

    [Fact]
    public void RemoveItem_AfterAddingItem_LeavesCollectionEmpty()
    {
        // Arrange
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var collection = Collection.Create("Test", ownerId: 1);
        collection.AddItem(quoteId: 1, clock);

        // Act
        collection.RemoveItem(quoteId: 1);

        // Assert
        collection.Items.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_OneOfMultipleItems_RemovesOnlyThatItem()
    {
        // Arrange
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var collection = Collection.Create("Test", ownerId: 1);
        collection.AddItem(quoteId: 1, clock);
        collection.AddItem(quoteId: 2, clock);
        collection.AddItem(quoteId: 3, clock);

        // Act
        collection.RemoveItem(quoteId: 2);

        // Assert
        collection.Items.Should().HaveCount(2);
        collection.Items.Should().NotContain(i => i.QuoteId == 2);
    }

    [Fact]
    public void RemoveItem_ThenAddSameQuoteAgain_Succeeds()
    {
        // Arrange — after removal the slot opens up; re-adding must be allowed
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var collection = Collection.Create("Test", ownerId: 1);
        collection.AddItem(quoteId: 5, clock);
        collection.RemoveItem(quoteId: 5);

        // Act
        var act = () => collection.AddItem(quoteId: 5, clock);

        // Assert
        act.Should().NotThrow();
        collection.Items.Should().ContainSingle(i => i.QuoteId == 5);
    }
}
