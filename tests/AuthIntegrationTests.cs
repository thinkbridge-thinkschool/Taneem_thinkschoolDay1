using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using Xunit;

namespace tests;

public class AuthIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

   public AuthIntegrationTests(CustomWebAppFactory factory)
{
    _factory = factory;
    _factory.SeedDatabase();
}

  private async Task<string> GetTokenAsync()
{
    var client   = _factory.CreateClient();
    var response = await client.PostAsJsonAsync("/api/auth/login", new
    {
        email    = "test@example.com",
        password = "Password123!"
    });

    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
        throw new Exception($"Login failed: {response.StatusCode} — {body}");

    var login = System.Text.Json.JsonSerializer.Deserialize<LoginResponse>(body,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    return login!.AccessToken;
}
    [Fact]
    public async Task Post_Quote_Without_Token_Returns_401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/quotes", new
        {
            author = "Marcus Aurelius",
            text   = "You have power over your mind."
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Quote_With_Valid_Token_Returns_201()
    {
        var token  = await GetTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new
        {
            author = "Marcus Aurelius",
            text   = "You have power over your mind."
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Quote_Not_Owned_Returns_403()
    {

        // Add a quote owned by user 2 (not the test user)
        int quoteId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db    = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quote = Quote.Create("Someone Else", "Not your quote", 2);
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            quoteId = quote.Id;
        }

        var token  = await GetTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await client.DeleteAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Quote_With_Expired_Token_Returns_401()
    {
        var expiredToken =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJuYW1laWQiOiIxIiwiZW1haWwiOiJ0ZXN0QGV4YW1wbGUuY29tIiwianRpIjoiYWJjIiwibmJmIjoxNjAwMDAwMDAwLCJleHAiOjE2MDAwMDAwMDAsImlhdCI6MTYwMDAwMDAwMH0." +
            "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.PostAsJsonAsync("/api/quotes", new
        {
            author = "Test",
            text   = "Test"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_With_Revoked_Token_Returns_401()
    {
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "test@example.com",
            password = "Password123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var loginBody    = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var refreshToken = loginBody!.RefreshToken;

       var firstRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new
{
    refreshToken = refreshToken
});
        if (firstRefresh.StatusCode != HttpStatusCode.OK)
        {
            var body = await firstRefresh.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"Expected OK, got {firstRefresh.StatusCode}: {body}");
        }

        var secondRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = refreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);
    }

    private record LoginResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")]
    string AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
    string RefreshToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    int ExpiresIn);

}