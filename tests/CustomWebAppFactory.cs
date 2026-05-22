using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;
using System.Text;

namespace tests;

public class CustomWebAppFactory : WebApplicationFactory<Program>
{
    private const string DbName    = "TestDb_Auth";
    private const string JwtKey    = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]              = JwtKey,
                ["Jwt:ExpiresInMinutes"] = "60",
                ["Entra:TenantId"]       = "test-tenant",
                ["Entra:ClientId"]       = "test-client",
                ["Entra:Audience"]       = "api://test-client"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove ALL EF related
            var toRemove = services
                .Where(d =>
                    d.ServiceType.FullName != null &&
                    (d.ServiceType.FullName.Contains("EntityFrameworkCore") ||
                     d.ServiceType.FullName.Contains("DbContext") ||
                     d.ServiceType == typeof(AppDbContext)))
                .ToList();

            foreach (var d in toRemove)
                services.Remove(d);

            // Add InMemory with fixed name
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(DbName));

            // Override Bearer scheme to use test key
            services.PostConfigure<JwtBearerOptions>("Bearer", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = false,
                    ValidateAudience         = false,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(JwtKey)),
                    ClockSkew = TimeSpan.Zero
                };
            });

            // Override Entra scheme to use test key (no authority check)
            services.PostConfigure<JwtBearerOptions>("Entra", options =>
            {
                options.Authority              = null;
                options.MetadataAddress        = null;
                options.RequireHttpsMetadata   = false;
                options.BackchannelHttpHandler = new System.Net.Http.HttpClientHandler();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = false,
                    ValidateAudience         = false,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(JwtKey)),
                    ClockSkew = TimeSpan.Zero
                };
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