using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(
    builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.PropertyNameCaseInsensitive = true;
    });

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

// Map domain exceptions to HTTP status codes.
// CollectionDomainException subtypes bubble up from the aggregate
// through the endpoint and are caught here before ExceptionMiddleware
// would turn them into 500s.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (CollectionNotFoundException ex)
    {
        context.Response.StatusCode  = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            title  = "Not found.",
            status = 404,
            detail = ex.Message
        });
    }
    catch (CollectionDomainException ex)
    {
        // Name invalid, collection full, duplicate quote, quote not in collection
        // — all are client errors (422).
        context.Response.StatusCode  = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            title  = "Business rule violation.",
            status = 422,
            detail = ex.Message
        });
    }
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    db.Database.Migrate();
}

app.MapQuoteEndpoints();
app.MapCollectionEndpoints();   // ← new

app.Run();