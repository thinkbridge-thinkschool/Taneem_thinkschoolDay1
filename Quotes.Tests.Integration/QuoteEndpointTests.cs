using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Quotes.Tests.Integration;

// xUnit creates a new QuoteEndpointTests instance for each [Fact].
// Each instance creates a new QuotesIntegrationFactory with a unique GUID DB name.
// Result: every test gets a completely empty, isolated in-memory database.

public sealed class QuoteEndpointTests : IDisposable
{
    private readonly QuotesIntegrationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ── GET /api/quotes ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyArray()
    {
        // Arrange — schema only, no quotes
        _factory.EnsureDbCreated();
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/quotes?page=1&size=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = await response.Content.ReadFromJsonAsync<QuoteResponse[]>(JsonOpts);
        quotes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_WithSeededQuotes_ReturnsAllOfThem()
    {
        // Arrange
        await _factory.SeedQuoteAsync("Seneca",          "Dum differtur vita transcurrit.", createdByUserId: 0);
        await _factory.SeedQuoteAsync("Marcus Aurelius", "The impediment to action advances action.", createdByUserId: 0);
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/quotes?page=1&size=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = await response.Content.ReadFromJsonAsync<QuoteResponse[]>(JsonOpts);
        quotes.Should().HaveCount(2);
        quotes.Should().Contain(q => q.Author == "Seneca");
    }

    // ── GET /api/quotes/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingQuote_ReturnsIt()
    {
        // Arrange
        var quoteId = await _factory.SeedQuoteAsync(
            "Epictetus", "Make the best use of what is in your power.", createdByUserId: 0);
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/quotes/{quoteId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(JsonOpts);
        quote!.Id.Should().Be(quoteId);
        quote.Author.Should().Be("Epictetus");
    }

    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        // Arrange — empty DB, no quote with ID 99999
        _factory.EnsureDbCreated();
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/quotes/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/quotes ───────────────────────────────────────────────────

    [Fact]
    public async Task Post_WithValidToken_Returns201AndLocationHeader()
    {
        // Arrange
        _factory.SeedUser();
        var client = await _factory.CreateAuthorizedClientAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/quotes", new
        {
            author = "Seneca",
            text   = "Per aspera ad astra.",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/api/quotes/");
    }

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        // Arrange — no auth header
        _factory.EnsureDbCreated();
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/quotes", new
        {
            author = "Seneca",
            text   = "Per aspera ad astra.",
        });

        // Assert — "can-write-quotes" policy requires authentication
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_WithBlankAuthor_Returns422WithProblemDetails()
    {
        // Arrange — Quote.Create("", ...) throws QuoteAuthorInvalidException
        //            ExceptionMiddleware catches QuoteDomainException → 422
        _factory.SeedUser();
        var client = await _factory.CreateAuthorizedClientAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/quotes", new
        {
            author = "",
            text   = "Some valid text.",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);  // 422
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("empty");  // QuoteAuthorInvalidException message
    }

    [Fact]
    public async Task Post_WithTextOver1000Chars_Returns422WithProblemDetails()
    {
        // Arrange — Quote.Create throws QuoteTextInvalidException → middleware → 422
        _factory.SeedUser();
        var client = await _factory.CreateAuthorizedClientAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/quotes", new
        {
            author = "Valid Author",
            text   = new string('x', 1001),
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("1000");  // QuoteTextInvalidException mentions the cap
    }

    // ── DELETE /api/quotes/{id} ────────────────────────────────────────────

    [Fact]
    public async Task Delete_OwnQuote_Returns204()
    {
        // Arrange — seed user, seed quote owned BY that user, log in as that user
        var userId  = _factory.SeedUser();
        var quoteId = await _factory.SeedQuoteAsync("Author", "Text", createdByUserId: userId);
        var client  = await _factory.CreateAuthorizedClientAsync();

        // Act
        var response = await client.DeleteAsync($"/api/quotes/{quoteId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_QuoteOwnedByOtherUser_Returns403()
    {
        // Arrange — quote owned by userId 999, test user has a different ID
        _factory.SeedUser();
        var quoteId = await _factory.SeedQuoteAsync("Other", "Their quote", createdByUserId: 999);
        var client  = await _factory.CreateAuthorizedClientAsync();

        // Act
        var response = await client.DeleteAsync($"/api/quotes/{quoteId}");

        // Assert — OwnQuoteRequirement: CreatedByUserId != current user → 403
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_AfterSoftDelete_Returns404()
    {
        // Arrange — create owned quote, delete it, then try to read it
        var userId  = _factory.SeedUser();
        var quoteId = await _factory.SeedQuoteAsync("Ghost", "Soon deleted.", createdByUserId: userId);
        var client  = await _factory.CreateAuthorizedClientAsync();

        await client.DeleteAsync($"/api/quotes/{quoteId}");   // soft-delete

        // Act — repository filters IsDeleted, so the quote is invisible
        var response = await client.GetAsync($"/api/quotes/{quoteId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
