using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryTranslation.Models;

namespace QueryTranslation.Data;

public class AppDbContext(string dbPath) : DbContext
{
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options
            .UseSqlite($"Data Source={dbPath}")
            .LogTo(Console.WriteLine, LogLevel.Information)
            .EnableSensitiveDataLogging();
}
