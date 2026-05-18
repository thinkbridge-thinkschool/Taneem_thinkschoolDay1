using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(
                configuration.GetConnectionString("Default"));
        });

        services.AddScoped<IQuoteRepository, QuoteRepository>();

        return services;
    }
}