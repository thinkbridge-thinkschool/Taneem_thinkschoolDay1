using EfTrackingBenchmark.Data;
using EfTrackingBenchmark.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

// ── Setup ─────────────────────────────────────────────────────────────────────
const string dbPath = "benchmark.db";
if (File.Exists(dbPath)) File.Delete(dbPath);

await using (var db = new BenchmarkContext(dbPath))
{
    await db.Database.EnsureCreatedAsync();

    var quotes = Enumerable.Range(1, 10_000)
        .Select(i => new Quote { Author = $"Author{i % 10}", Text = $"Quote number {i}" })
        .ToList();

    await db.Quotes.AddRangeAsync(quotes);
    await db.SaveChangesAsync();
    Console.WriteLine("Seeded 10,000 quotes.");
}

// ── Identity resolution demo ──────────────────────────────────────────────────
Console.WriteLine("\n── Identity Resolution ──────────────────────────");
await using (var db = new BenchmarkContext(dbPath))
{
    var q1 = await db.Quotes.FindAsync(1);
    var q2 = await db.Quotes.FindAsync(1);
    Console.WriteLine($"q1 == q2 (same object): {ReferenceEquals(q1, q2)}");
    Console.WriteLine("EF returned the same instance from the change tracker cache.");
}

// ── Benchmark: Tracked vs AsNoTracking ───────────────────────────────────────
const int runs = 3;
var trackedTimes = new long[runs];
var trackedAllocs = new long[runs];
var untrackedTimes = new long[runs];
var untrackedAllocs = new long[runs];

Console.WriteLine("\n── Tracked reads (default) ──────────────────────");
for (int i = 0; i < runs; i++)
{
    await using var db = new BenchmarkContext(dbPath);

    long before = GC.GetTotalAllocatedBytes();
    var sw = Stopwatch.StartNew();

    var quotes = await db.Quotes.ToListAsync();

    sw.Stop();
    trackedTimes[i] = sw.ElapsedMilliseconds;
    trackedAllocs[i] = (GC.GetTotalAllocatedBytes() - before) / 1024;
    Console.WriteLine($"  Run {i + 1}: {trackedTimes[i]}ms | {trackedAllocs[i]:N0} KB allocated | {quotes.Count} rows");
}

Console.WriteLine("\n── AsNoTracking reads ───────────────────────────");
for (int i = 0; i < runs; i++)
{
    await using var db = new BenchmarkContext(dbPath);

    long before = GC.GetTotalAllocatedBytes();
    var sw = Stopwatch.StartNew();

    var quotes = await db.Quotes.AsNoTracking().ToListAsync();

    sw.Stop();
    untrackedTimes[i] = sw.ElapsedMilliseconds;
    untrackedAllocs[i] = (GC.GetTotalAllocatedBytes() - before) / 1024;
    Console.WriteLine($"  Run {i + 1}: {untrackedTimes[i]}ms | {untrackedAllocs[i]:N0} KB allocated | {quotes.Count} rows");
}

// ── Delta summary ─────────────────────────────────────────────────────────────
long avgTrackedTime   = trackedTimes.Sum()   / runs;
long avgTrackedAlloc  = trackedAllocs.Sum()  / runs;
long avgUntrackedTime  = untrackedTimes.Sum()  / runs;
long avgUntrackedAlloc = untrackedAllocs.Sum() / runs;

double timeSaving  = (1.0 - (double)avgUntrackedTime  / avgTrackedTime)  * 100;
double allocSaving = (1.0 - (double)avgUntrackedAlloc / avgTrackedAlloc) * 100;

Console.WriteLine("\n── Delta ────────────────────────────────────────");
Console.WriteLine($"{"Metric",-14} | {"Tracked",-10} | {"AsNoTracking",-12} | Saving");
Console.WriteLine($"{new string('-', 14)}-+-{new string('-', 10)}-+-{new string('-', 12)}-+--------");
Console.WriteLine($"{"Time (avg)",-14} | {avgTrackedTime + "ms",-10} | {avgUntrackedTime + "ms",-12} | ~{timeSaving:F0}% faster");
Console.WriteLine($"{"Allocations",-14} | {avgTrackedAlloc + " KB",-10} | {avgUntrackedAlloc + " KB",-12} | ~{allocSaving:F0}% less memory");

Console.WriteLine("\nDone.");
