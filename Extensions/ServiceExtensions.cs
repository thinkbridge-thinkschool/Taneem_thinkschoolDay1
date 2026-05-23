using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class ServiceExtensions
{
    // Shared ActivitySource — controllers import this to create custom spans
    public static readonly ActivitySource ActivitySource =
        new("QuotesApi", "1.0.0");

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Default")));

        var otlpEndpoint = configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("QuotesApi"))
            .WithTracing(t => t
                .AddSource(ActivitySource.Name)
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

        // When App Insights connection string is present, also export to Azure Monitor.
        // Locally this is absent — Jaeger only. In prod Key Vault supplies the value.
        var appInsightsConnStr = configuration["AppInsights:ConnectionString"];
        if (!string.IsNullOrEmpty(appInsightsConnStr))
            otelBuilder.UseAzureMonitor(o => o.ConnectionString = appInsightsConnStr);
        services.AddHttpContextAccessor();
        services.AddScoped<IQuoteRepository,      QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAuthorizationHandler, OwnQuoteHandler>();

services.AddAuthorization(options =>
{
    options.AddPolicy("can-write-quotes", p =>
        p.RequireAuthenticatedUser());

    options.AddPolicy("can-delete-quotes", p =>
        p.RequireAuthenticatedUser()
         .AddRequirements(new OwnQuoteRequirement()));
});
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
                    ValidateIssuer           = true,
                    ValidIssuer             = "self",
                    ValidateAudience        = false,
                    ValidateLifetime        = true,
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
                    var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

                    if (authHeader is null || !authHeader.StartsWith("Bearer "))
                        return "Bearer";

                    var token = authHeader.Substring("Bearer ".Length).Trim();

                    try
                    {
                        var jwt    = new JwtSecurityToken(token);
                        var issuer = jwt.Issuer;
                        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

                        // Log the raw payload and claims for debugging
                        logger.LogInformation("Token raw payload: {Payload}", jwt.Payload.SerializeToJson());
                        logger.LogInformation("Token claims: {Claims}", string.Join(", ", jwt.Claims.Select(c => $"{c.Type}:{c.Value}")));
                        logger.LogInformation("Token issuer: {Issuer}", issuer);

                        return issuer != null && (issuer.Contains("microsoftonline.com") || issuer.Contains("sts.windows.net"))
                            ? "Entra"
                            : "Bearer";
                    }
                    catch (Exception ex)
                    {
                        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogError(ex, "Error parsing JWT in Smart scheme selector");
                        return "Bearer";
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}