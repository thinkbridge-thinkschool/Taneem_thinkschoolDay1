using Microsoft.EntityFrameworkCore;
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
        {
            options.UseSqlite(
                configuration.GetConnectionString("Default"));
        });

        services.AddScoped<IQuoteRepository,      QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();// Singleton — stateless clock, safe to share across all requests
        services.AddSingleton<IClock, SystemClock>();  // ← new

        return services;
    }
}