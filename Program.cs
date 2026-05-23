using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Models;
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
}

app.MapControllers();
app.MapQuoteEndpoints();
app.MapCollectionEndpoints();

app.Run();

public partial class Program { }