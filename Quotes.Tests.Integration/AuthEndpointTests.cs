using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Quotes.Tests.Integration;

[Collection("Integration")]
public sealed class AuthEndpointTests : IDisposable
{
    private readonly QuotesIntegrationFactory _factory;

    public AuthEndpointTests(SqlServerFixture fixture)
    {
        _factory = new QuotesIntegrationFactory(fixture.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ── POST /api/auth/login ───────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        // Arrange
        _factory.SeedUser("alice@test.com", "Password123!");
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "alice@test.com",
            password = "Password123!",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.ExpiresIn.Should().BePositive();
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        // Arrange
        _factory.SeedUser("bob@test.com", "CorrectPassword!");
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "bob@test.com",
            password = "WrongPassword!",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/auth/refresh ─────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithRotatedTokens()
    {
        // Arrange — login to get the initial refresh token
        _factory.SeedUser();
        var client = _factory.CreateClient();

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "user@test.com",
            password = "Password123!",
        });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = login!.RefreshToken,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        // Rotation: the new refresh token must differ from the original
        body.RefreshToken.Should().NotBe(login.RefreshToken,
            because: "every refresh creates a new token (rotation)");
    }

    [Fact]
    public async Task Refresh_ReuseDetection_Returns401OnSecondUse()
    {
        // Arrange — login, refresh once (rotates the original), then try the original again
        _factory.SeedUser();
        var client = _factory.CreateClient();

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "user@test.com",
            password = "Password123!",
        });
        var login         = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        var originalToken = login!.RefreshToken;

        // First refresh — legitimate, rotates the token
        var firstRefresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = originalToken });
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "first use of a valid refresh token must succeed");

        // Act — second use of the same (now-replaced) token triggers reuse detection
        var secondRefresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = originalToken });

        // Assert — controller sees ReplacedByToken != null → revokes entire family → 401
        secondRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        // Arrange — no user seeded with this email
        _factory.EnsureDbCreated();
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "nobody@nowhere.com",
            password = "Password123!",
        });

        // Assert — user is null branch in AuthController.Login
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        // Arrange — no token seeded
        _factory.EnsureDbCreated();
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = "this-token-does-not-exist-in-db",
        });

        // Assert — existing is null branch in AuthController.Refresh
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_OrphanedToken_Returns401()
    {
        // Arrange — login to get a refresh token, then delete the user
        var userId = _factory.SeedUser();
        var client = _factory.CreateClient();

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "user@test.com",
            password = "Password123!",
        });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);

        await _factory.DeleteUserAsync(userId);

        // Act — token exists and is active, but the user row is gone
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = login!.RefreshToken,
        });

        // Assert — user is null branch in AuthController.Refresh
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RevokedToken_Returns401()
    {
        // Arrange — login, then revoke the token directly in DB
        var userId = _factory.SeedUser();
        var client = _factory.CreateClient();

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "user@test.com",
            password = "Password123!",
        });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);

        await _factory.RevokeAllTokensForUserAsync(userId);

        // Act — try to use the revoked token
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = login!.RefreshToken,
        });

        // Assert — !existing.IsActive branch (IsRevoked = true)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/auth/logout ──────────────────────────────────────────────

    [Fact]
    public async Task Logout_UnknownToken_Returns204()
    {
        // Arrange — seed a user so LoginAsync succeeds, but send a token that was never issued
        _factory.SeedUser();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _factory.LoginAsync());

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/logout", new
        {
            refreshToken = "token-that-does-not-exist",
        });

        // Assert — existing is null path: logout is idempotent, still 204
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_AlreadyRevokedToken_Returns204()
    {
        // Arrange — login, logout once, logout again with the same token
        _factory.SeedUser();
        var client = _factory.CreateClient();

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "user@test.com",
            password = "Password123!",
        });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        // First logout — revokes the token
        await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = login.RefreshToken });

        // Act — second logout with the already-revoked token
        var response = await client.PostAsJsonAsync("/api/auth/logout", new
        {
            refreshToken = login.RefreshToken,
        });

        // Assert — existing.IsActive is false path: still 204
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_WithValidToken_Returns204()
    {
        // Arrange — login to get both access and refresh tokens
        _factory.SeedUser();
        var client = _factory.CreateClient();

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "user@test.com",
            password = "Password123!",
        });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);

        // Logout is [Authorize] — must include the access token as Bearer
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/logout", new
        {
            refreshToken = login.RefreshToken,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
