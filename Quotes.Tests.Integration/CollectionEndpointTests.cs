using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Quotes.Tests.Integration;

public sealed class CollectionEndpointTests : IDisposable
{
    private readonly QuotesIntegrationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ── POST /api/collections ──────────────────────────────────────────────

    [Fact]
    public async Task PostCollection_ValidName_Returns201AndLocationHeader()
    {
        // Arrange
        _factory.SeedUser();
        var client = await _factory.CreateAuthorizedClientAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/collections", new
        {
            name    = "My Reading List",
            ownerId = 1,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/api/collections/");

        var body = await response.Content.ReadFromJsonAsync<CollectionResponse>(JsonOpts);
        body!.Name.Should().Be("My Reading List");
    }

    [Fact]
    public async Task PostCollection_WithoutToken_Returns401()
    {
        // Arrange
        _factory.EnsureDbCreated();
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/collections", new
        {
            name    = "My Reading List",
            ownerId = 1,
        });

        // Assert — "can-write-quotes" policy requires authentication
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostCollection_BlankName_Returns400WithValidationProblem()
    {
        // Arrange — endpoint checks for blank before calling Collection.Create, returns ValidationProblem
        _factory.SeedUser();
        var client = await _factory.CreateAuthorizedClientAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/collections", new
        {
            name    = "",
            ownerId = 1,
        });

        // Assert — Results.ValidationProblem → HTTP 400, body contains field name "name"
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("name");
    }

    // ── POST /api/collections/{id}/items ──────────────────────────────────

    [Fact]
    public async Task AddItem_ValidRequest_Returns200WithItem()
    {
        // Arrange — create a collection first, then add an item to it
        _factory.SeedUser();
        var client = await _factory.CreateAuthorizedClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/collections", new
        {
            name    = "Favourites",
            ownerId = 1,
        });
        var collection = await createResp.Content.ReadFromJsonAsync<CollectionResponse>(JsonOpts);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/collections/{collection!.Id}/items",
            new { quoteId = 42 });   // quoteId doesn't need to exist — endpoint stores it as-is

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<CollectionResponse>(JsonOpts);
        updated!.Items.Should().ContainSingle(i => i.QuoteId == 42);
    }

    [Fact]
    public async Task AddItem_SetsAddedAtFromFakeClock_NotFromSystemClock()
    {
        // Arrange — pin the fake clock to a specific time, add item, verify AddedAt matches
        var expectedTime = new DateTimeOffset(2001, 9, 11, 8, 46, 0, TimeSpan.Zero);
        _factory.Clock.UtcNow = expectedTime;  // a date clearly not "now"

        _factory.SeedUser();
        var client = await _factory.CreateAuthorizedClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/collections", new
        {
            name    = "IClock Test Collection",
            ownerId = 1,
        });
        var collection = await createResp.Content.ReadFromJsonAsync<CollectionResponse>(JsonOpts);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/collections/{collection!.Id}/items",
            new { quoteId = 7 });

        // Assert — AddedAt must equal the FakeClock value, not DateTime.UtcNow
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<CollectionResponse>(JsonOpts);
        updated!.Items[0].AddedAt.Should().Be(expectedTime,
            because: "the endpoint injects IClock; we replaced it with FakeClock");
    }

    [Fact]
    public async Task AddItem_ToNonExistentCollection_Returns404()
    {
        // Arrange
        _factory.SeedUser();
        var client = await _factory.CreateAuthorizedClientAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/collections/99999/items",
            new { quoteId = 1 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
