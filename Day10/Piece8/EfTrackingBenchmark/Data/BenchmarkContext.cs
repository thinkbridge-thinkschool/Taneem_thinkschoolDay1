using EfTrackingBenchmark.Models;
using Microsoft.EntityFrameworkCore;

namespace EfTrackingBenchmark.Data;

public class BenchmarkContext(string dbPath) : DbContext
{
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={dbPath}");
}
