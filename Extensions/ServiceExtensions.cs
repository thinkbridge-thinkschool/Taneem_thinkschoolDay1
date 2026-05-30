using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using QuotesApi.Authorization;
using QuotesApi.Commands;
using QuotesApi.Data;
using QuotesApi.Queries;
using QuotesApi.Options;
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
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));

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

        var externalQuotesBaseUrl = configuration["ExternalQuotes:BaseUrl"] ?? "https://zenquotes.io";
        services.AddHttpClient("external-quotes", c =>
        {
            c.BaseAddress = new Uri(externalQuotesBaseUrl);
            c.Timeout     = Timeout.InfiniteTimeSpan; // resilience pipeline controls timeout
        })
        .AddResilienceHandler("default", (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILogger<ExternalQuoteService>>();

            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                OnRetry          = args =>
                {
                    logger.LogWarning(
                        "Retry {Attempt} after {Delay}ms — {Outcome}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                    return ValueTask.CompletedTask;
                }
            });

            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration        = TimeSpan.FromSeconds(30),
                FailureRatio            = 0.5,
                MinimumThroughput       = 3,
                BreakDuration           = TimeSpan.FromSeconds(15),
                OnOpened = args =>
                {
                    logger.LogError("Circuit breaker opened for {Duration}s", args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            });

            builder.AddTimeout(TimeSpan.FromSeconds(10));
        });

        services.AddScoped<IExternalQuoteService, ExternalQuoteService>();

        services.AddScoped<IQuoteRepository,      QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAuthorizationHandler, OwnQuoteHandler>();

        // CQRS-lite handlers
        services.AddScoped<CreateQuoteHandler>();
        services.AddScoped<GetQuotesSummaryHandler>();

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
                        Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? string.Empty)),
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