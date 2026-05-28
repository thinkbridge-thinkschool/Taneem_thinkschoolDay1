using Microsoft.EntityFrameworkCore;
using QueryTranslation.Data;
using QueryTranslation.Models;

const string dbPath = "quotes.db";
if (File.Exists(dbPath)) File.Delete(dbPath);

// ── Seed ──────────────────────────────────────────────────────────────────────
await using (var db = new AppDbContext(dbPath))
{
    await db.Database.EnsureCreatedAsync();
    var quotes = new[]
    {
        new Quote { Author = "Einstein",  Text = "Imagination is more important than knowledge.", CreatedAt = DateTime.UtcNow },
        new Quote { Author = "Einstein",  Text = "Life is like riding a bicycle.",                CreatedAt = DateTime.UtcNow },
        new Quote { Author = "Churchill", Text = "Success is not final, failure is not fatal.",   CreatedAt = DateTime.UtcNow },
        new Quote { Author = "Churchill", Text = "We shall never surrender.",                     CreatedAt = DateTime.UtcNow },
        new Quote { Author = "Twain",     Text = "The secret of getting ahead is getting started.", CreatedAt = DateTime.UtcNow },
    };
    await db.Quotes.AddRangeAsync(quotes);
    await db.SaveChangesAsync();
    Console.WriteLine("Seeded 5 quotes.\n");
}

// ── 1. Original query — whole entity ─────────────────────────────────────────
Console.WriteLine("══ 1. Original query (whole entity — SELECT *) ══════");
await using (var db = new AppDbContext(dbPath))
{
    var quotes = await db.Quotes
        .Where(q => q.Author == "Einstein")
        .ToListAsync();

    Console.WriteLine($"\nResult: {quotes.Count} rows\n");
}

// ── 2. Projected query — only needed columns ──────────────────────────────────
Console.WriteLine("══ 2. Projected query (.Select → DTO) ══════════════");
await using (var db = new AppDbContext(dbPath))
{
    var quotes = await db.Quotes
        .Where(q => q.Author == "Einstein")
        .Select(q => new QuoteDto { Author = q.Author, Text = q.Text })
        .ToListAsync();

    Console.WriteLine($"\nResult: {quotes.Count} rows\n");
}

// ── 3. Client-side evaluation — caught and fixed ──────────────────────────────
Console.WriteLine("══ 3. Client-side evaluation (accidental) ═══════════");

Console.WriteLine("\n--- BAD: IEnumerable type leak — second filter runs in memory ---");
await using (var db = new AppDbContext(dbPath))
{
    // Developer assigns to IEnumerable — silently loses IQueryable.
    // The second .Where() is now LINQ-to-Objects, not LINQ-to-SQL.
    // SQL fetches ALL Einstein rows; the text filter never reaches the DB.
    IEnumerable<Quote> source = db.Quotes.Where(q => q.Author == "Einstein");
    var quotes = source.Where(q => q.Text.Length > 40).ToList();

    Console.WriteLine($"\nResult: {quotes.Count} long Einstein quotes (text filter ran in C#)\n");
}

Console.WriteLine("--- FIXED: stay on IQueryable — both filters go to SQL ---");
await using (var db = new AppDbContext(dbPath))
{
    var quotes = await db.Quotes
        .Where(q => q.Author == "Einstein")
        .Where(q => q.Text.Length > 40)
        .ToListAsync();

    Console.WriteLine($"\nResult: {quotes.Count} long Einstein quotes (both filters in SQL)\n");
}

Console.WriteLine("Done.");
