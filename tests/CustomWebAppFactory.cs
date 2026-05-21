using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace tests;

public class CustomWebAppFactory : WebApplicationFactory<Program>
{
    private const string DbName = "TestDb_Auth";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]              = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                ["Jwt:ExpiresInMinutes"] = "60",
                ["Entra:TenantId"]       = "test-tenant",
                ["Entra:ClientId"]       = "test-client",
                ["Entra:Audience"]       = "api://test-client"
            });
        });
builder.ConfigureServices(services =>
{
    // Remove ALL EF related
    var efToRemove = services
        .Where(d =>
            d.ServiceType.FullName != null &&
            (d.ServiceType.FullName.Contains("EntityFrameworkCore") ||
             d.ServiceType.FullName.Contains("DbContext") ||
             d.ServiceType == typeof(AppDbContext)))
        .ToList();

    foreach (var d in efToRemove)
        services.Remove(d);

    // Add InMemory with fixed name
    services.AddDbContext<AppDbContext>(options =>
        options.UseInMemoryDatabase(DbName));

    // Disable Entra authority validation in tests
    services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>("Entra", options =>
    {
        options.Authority            = null;
        options.MetadataAddress      = null;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidateIssuer   = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.ValidIssuers     = null;
    });
});
    }

    public void SeedDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        if (!db.Users.Any(u => u.Email == "test@example.com"))
        {
            db.Users.Add(new User
            {
                Email        = "test@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                Role         = "user"
            });
            db.SaveChanges();
        }
    }
}