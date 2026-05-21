using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Default")));

        services.AddScoped<IQuoteRepository,      QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddSingleton<IClock, SystemClock>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme          = "Smart";
                options.DefaultChallengeScheme = "Smart";
            })

            // ── Scheme 1: Your own JWT ─────────────────────────────────
            .AddJwtBearer("Bearer", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = false,
                    ValidateAudience         = false,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!)),
                    ClockSkew = TimeSpan.Zero
                };
            })

            // ── Scheme 2: Entra ID (Microsoft) ────────────────────────
            .AddJwtBearer("Entra", options =>
            {
                options.Authority = $"https://login.microsoftonline.com/" +
                                    $"{configuration["Entra:TenantId"]}/v2.0";
                options.Audience  = configuration["Entra:Audience"];

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateLifetime = true,
                    ClockSkew        = TimeSpan.Zero,
                    ValidIssuers     = new[]
                    {
                        $"https://login.microsoftonline.com/{configuration["Entra:TenantId"]}/v2.0",
                        $"https://sts.windows.net/{configuration["Entra:TenantId"]}/"
                    }
                };
            })

            // ── Policy scheme: picks Bearer or Entra based on issuer ──
            .AddPolicyScheme("Smart", "Smart", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers["Authorization"]
                        .FirstOrDefault();

                    if (authHeader is null || !authHeader.StartsWith("Bearer "))
                        return "Bearer";

                    var token = authHeader.Substring("Bearer ".Length).Trim();

                    try
                    {
                        var jwt    = new JwtSecurityToken(token);
                        var issuer = jwt.Issuer;

                        var logger = context.RequestServices
                            .GetRequiredService<ILogger<Program>>();
                        logger.LogInformation("Token issuer: {Issuer}", issuer);

                        return issuer.Contains("microsoftonline.com") ||
                               issuer.Contains("sts.windows.net")
                            ? "Entra"
                            : "Bearer";
                    }
                    catch
                    {
                        return "Bearer";
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}