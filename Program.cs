using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Models;
using BCrypt.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

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

using (var scope = app.Services.CreateScope())
{
    var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Seed test user
    if (!db.Users.Any(u => u.Email == "test@example.com"))
    {
        db.Users.Add(new User
        {
            Email        = "test@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!")
        });
        db.SaveChanges();
    }
}

app.MapControllers();
app.MapQuoteEndpoints();
app.MapCollectionEndpoints();

app.Run();

public partial class Program { }