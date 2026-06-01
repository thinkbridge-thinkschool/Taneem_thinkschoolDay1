using QuotesApi.Models;

namespace Quotes.Tests.Unit;

// One class covers the entire Quote production class.
// Every test follows AAA: Arrange → Act → Assert, no shared SetUp.

public sealed class QuoteTests
{
    // ── Quote.Create — success paths ──────────────────────────────────────

    [Fact]
    public void Create_ValidInputs_ReturnsQuoteWithTrimmedValues()
    {
        // Arrange
        var author = "  Mark Twain  ";
        var text   = "  The secret of getting ahead is getting started.  ";

        // Act
        var quote = Quote.Create(author, text, createdByUserId: 7);

        // Assert
        quote.Author.Should().Be("Mark Twain");
        quote.Text.Should().Be("The secret of getting ahead is getting started.");
        quote.CreatedByUserId.Should().Be(7);
        quote.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_OmittedCreatedByUserId_DefaultsToZero()
    {
        // Arrange / Act — default value of the optional parameter
        var quote = Quote.Create("Author", "Text");

        // Assert
        quote.CreatedByUserId.Should().Be(0);
    }

    [Fact]
    public void Create_AuthorExactly200Chars_Succeeds()
    {
        // Arrange — 200 chars is the inclusive upper boundary
        var author = new string('a', 200);

        // Act
        var act = () => Quote.Create(author, "Some text");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_TextExactly1000Chars_Succeeds()
    {
        // Arrange — 1000 chars is the inclusive upper boundary
        var text = new string('x', 1000);

        // Act
        var act = () => Quote.Create("Author", text);

        // Assert
        act.Should().NotThrow();
    }

    // ── Quote.Create — author failure modes ──────────────────────────────

    [Theory]
    [InlineData("")]        // empty string
    [InlineData("   ")]     // whitespace-only
    public void Create_BlankAuthor_ThrowsQuoteAuthorInvalidException(string author)
    {
        // Arrange / Act
        var act = () => Quote.Create(author, "Some text");

        // Assert
        act.Should().Throw<QuoteAuthorInvalidException>()
           .WithMessage("*empty*");
    }

    [Fact]
    public void Create_AuthorOf201Chars_ThrowsQuoteAuthorInvalidException()
    {
        // Arrange — one character past the limit
        var author = new string('a', 201);

        // Act
        var act = () => Quote.Create(author, "Some text");

        // Assert — error message must mention the 200-char cap
        act.Should().Throw<QuoteAuthorInvalidException>()
           .WithMessage("*200*");
    }

    // ── Quote.Create — text failure modes ────────────────────────────────

    [Theory]
    [InlineData("")]        // empty string
    [InlineData("   ")]     // whitespace-only
    public void Create_BlankText_ThrowsQuoteTextInvalidException(string text)
    {
        // Arrange / Act
        var act = () => Quote.Create("Author", text);

        // Assert
        act.Should().Throw<QuoteTextInvalidException>()
           .WithMessage("*empty*");
    }

    [Fact]
    public void Create_TextOf1001Chars_ThrowsQuoteTextInvalidException()
    {
        // Arrange — one character past the limit
        var text = new string('x', 1001);

        // Act
        var act = () => Quote.Create("Author", text);

        // Assert — error message must mention the 1000-char cap
        act.Should().Throw<QuoteTextInvalidException>()
           .WithMessage("*1000*");
    }

    // ── SoftDelete ────────────────────────────────────────────────────────

    [Fact]
    public void SoftDelete_CalledOnce_SetsIsDeletedToTrue()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text");

        // Act
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void SoftDelete_CalledTwice_RemainsDeleted()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text");

        // Act — calling it twice must be idempotent (no exception, stays true)
        quote.SoftDelete();
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void SoftDelete_DoesNotClearAuthorOrText()
    {
        // Arrange
        var quote = Quote.Create("Voltaire", "I disapprove of what you say.");

        // Act
        quote.SoftDelete();

        // Assert — soft delete is invisible to consumers; data is preserved for audit
        quote.Author.Should().Be("Voltaire");
        quote.Text.Should().Be("I disapprove of what you say.");
    }
}
