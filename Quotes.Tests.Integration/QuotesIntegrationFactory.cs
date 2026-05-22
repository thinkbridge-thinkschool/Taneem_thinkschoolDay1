using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Quotes.Tests.Integration;

// ── FakeClock ─────────────────────────────────────────────────────────────────
// Replaces SystemClock in the DI container so tests can pin the exact timestamp
// that ends up in CollectionItem.AddedAt. Mutable so each test can set its own time.

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } =
        new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero);
}

// ── QuotesIntegrationFactory ──────────────────────────────────────────────────
// One instance per test class (xUnit creates a new test class instance per test).
// _dbName is a fresh GUID on each construction → every test gets an empty database.

public sealed class QuotesIntegrationFactory : WebApplicationFactory<Program>
{
    // Unique SQL Server database per factory instance = isolated DB per test
    private readonly string _connectionString;

    public QuotesIntegrationFactory(string baseConnectionString)
    {
        // Each factory gets its own database on the shared container → full isolation
        var csb = new SqlConnectionStringBuilder(baseConnectionString);
        csb.InitialCatalog = $"testdb_{Guid.NewGuid():N}";
        _connectionString = csb.ConnectionString;
    }

    // Must match the key used to sign tokens in AuthController (hex → bytes)
    public const string JwtKey =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    // Exposed so tests can verify that AddedAt was read from this clock, not DateTime.UtcNow
    public FakeClock Clock { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tells Program.cs to skip db.Database.Migrate() and the prod seed
        builder.UseEnvironment("Testing");

        // Inject the test JWT key so AuthController signs with a known key
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]              = JwtKey,
                ["Jwt:ExpiresInMinutes"] = "60",
                ["Entra:TenantId"]       = "test-tenant",
                ["Entra:ClientId"]       = "test-client",
                ["Entra:Audience"]       = "api://test-client",
            });
        });

        builder.ConfigureServices(services =>
        {
            // ── Swap SQLite for in-memory EF ──────────────────────────────
            var efDescriptors = services
                .Where(d => d.ServiceType.FullName != null &&
                            (d.ServiceType.FullName.Contains("EntityFrameworkCore") ||
                             d.ServiceType.FullName.Contains("DbContext") ||
                             d.ServiceType == typeof(AppDbContext)))
                .ToList();

            foreach (var d in efDescriptors)
                services.Remove(d);

            services.AddDbContext<AppDbContext>(opts =>
                opts.UseSqlServer(_connectionString));

            // ── Swap SystemClock for FakeClock ────────────────────────────
            var clockDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IClock));
            if (clockDesc != null) services.Remove(clockDesc);
            services.AddSingleton<IClock>(Clock);  // same instance exposed on .Clock property

            // ── Override Bearer JWT validation to accept test-signed tokens ──
            services.PostConfigure<JwtBearerOptions>("Bearer", opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = false,
                    ValidateAudience         = false,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                                                  Encoding.UTF8.GetBytes(JwtKey)),
                    ClockSkew = TimeSpan.Zero,
                };
            });

            // ── Disable Entra authority (no real Azure AD in tests) ────────
            services.PostConfigure<JwtBearerOptions>("Entra", opts =>
            {
                opts.Authority              = null;
                opts.MetadataAddress        = null;
                opts.RequireHttpsMetadata   = false;
                opts.BackchannelHttpHandler = new System.Net.Http.HttpClientHandler();
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = false,
                    ValidateAudience         = false,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                                                  Encoding.UTF8.GetBytes(JwtKey)),
                    ClockSkew = TimeSpan.Zero,
                };
            });
        });
    }

    // Creates the in-memory DB schema (replaces Migrate() which is skipped in Testing env)
    public void EnsureDbCreated()
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    // Seeds a user and returns the EF-assigned ID (used to set createdByUserId on quotes)
    public int SeedUser(
        string email    = "user@test.com",
        string password = "Password123!",
        string role     = "user")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var user = new User
        {
            Email        = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role         = role,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;   // EF auto-assigns this — don't assume 1
    }

    // Seeds a quote directly (bypasses the endpoint which doesn't set createdByUserId)
    public async Task<int> SeedQuoteAsync(string author, string text, int createdByUserId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var quote = Quote.Create(author, text, createdByUserId);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote.Id;
    }

    // Calls the real login endpoint and returns the access token
    public async Task<string> LoginAsync(
        string email    = "user@test.com",
        string password = "Password123!")
    {
        var response = await CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email, password });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LoginDto>(body, JsonOpts)!;
        return result.AccessToken;
    }

    // Returns an HttpClient with a valid Bearer token already attached
    public async Task<HttpClient> CreateAuthorizedClientAsync(
        string email    = "user@test.com",
        string password = "Password123!")
    {
        var token  = await LoginAsync(email, password);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Private DTO — only needed inside this factory helper
    private record LoginDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")]   int    ExpiresIn);
}
