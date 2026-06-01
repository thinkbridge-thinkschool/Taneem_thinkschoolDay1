using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Models;
using QuotesApi.Services;
using BCrypt.Net;
using System.Diagnostics;
using Azure.Identity;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// Pull secrets from Key Vault when a vault name is configured.
// Locally: DefaultAzureCredential uses `az login`.
// In prod: uses the app's Managed Identity — no credentials in code.
var keyVaultName = builder.Configuration["KeyVault:Name"];
if (!string.IsNullOrEmpty(keyVaultName))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri($"https://{keyVaultName}.vault.azure.net/"),
        new DefaultAzureCredential());
}

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

// Stamp every log line with the OTel TraceId so logs and traces correlate
app.Use((ctx, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    using (LogContext.PushProperty("TraceId", traceId))
        return next(ctx);
});

app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (CollectionNotFoundException ex)
    {
        context.Response.StatusCode  = 404;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { title = "Not found.", status = 404, detail = ex.Message });
    }
    catch (CollectionDomainException ex)
    {
        context.Response.StatusCode  = 422;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { title = "Business rule violation.", status = 422, detail = ex.Message });
    }
    catch (QuoteDomainException ex)
    {
        context.Response.StatusCode  = 422;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { title = "Business rule violation.", status = 422, detail = ex.Message });
    }
});

// Skip DB setup when running integration tests
if (!builder.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

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

    if (!db.Quotes.Any())
    {
        var seedUser = db.Users.First(u => u.Email == "test@example.com");
        var seedQuotes = new[]
        {
            "The only way to do great work is to love what you do.",
            "In the middle of every difficulty lies opportunity.",
            "It does not matter how slowly you go as long as you do not stop.",
            "Life is what happens when you're busy making other plans.",
            "The future belongs to those who believe in the beauty of their dreams.",
            "It is during our darkest moments that we must focus to see the light.",
            "The best time to plant a tree was 20 years ago.",
            "An unexamined life is not worth living.",
            "Spread love everywhere you go.",
            "When you reach the end of your rope, tie a knot in it and hang on.",
        };
        foreach (var text in seedQuotes)
            db.Quotes.Add(Quote.Create("Seed", text, seedUser.Id));
        db.SaveChanges();
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/quotes/external", async (IExternalQuoteService svc, CancellationToken ct) =>
{
    var quote = await svc.GetRandomQuoteAsync(ct);
    return Results.Ok(quote);
});

app.MapControllers();
app.MapQuoteEndpoints();
app.MapCollectionEndpoints();

// ── POST /api/dev/seed ────────────────────────────────────────────────────
// Seeds 500 quotes across 20 seedAuthors for Day 11 profiling exercise.
// Safe to call multiple times — skips if data already exists.
string[] seedAuthors =
[
    "Einstein", "Aristotle", "Plato", "Socrates", "Newton",
    "Darwin", "Curie", "Tesla", "Feynman", "Hawking",
    "Nietzsche", "Kant", "Descartes", "Hume", "Locke",
    "Voltaire", "Rousseau", "Marx", "Freud", "Jung"
];

app.MapPost("/api/dev/seed", async (AppDbContext db) =>
{
    if (db.Quotes.Count() >= 500)
        return Results.Ok(new { message = "Already seeded." });

    var seedUser = db.Users.First();

    var quotes = new List<Quote>();
    for (var a = 0; a < seedAuthors.Length; a++)
    {
        for (var i = 1; i <= 25; i++)
        {
            quotes.Add(Quote.Create(
                seedAuthors[a],
                $"Wisdom from {seedAuthors[a]}, quote number {i}: the examined life is worth living in iteration {i}.",
                seedUser.Id));
        }
    }

    db.Quotes.AddRange(quotes);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = $"Seeded {quotes.Count} quotes across {seedAuthors.Length} authors." });
});

app.Run();

public partial class Program { }