using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public sealed class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext _db;

    public CollectionRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Loads the collection with all its items in one query.
    /// The aggregate root needs the full item list to enforce invariants.
    /// </summary>
    public Task<Collection?> GetByIdAsync(int id, CancellationToken ct) =>
        _db.Collections
           .Include(c => c.Items)   // owned types still need Include
           .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Collection> AddAsync(Collection collection, CancellationToken ct)
    {
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync(ct);
        return collection;
    }

    /// <summary>
    /// The aggregate has already mutated its Items list.
    /// SaveChanges picks up the tracked changes automatically.
    /// </summary>
    public async Task UpdateAsync(Collection collection, CancellationToken ct)
    {
        // No explicit Attach needed — the entity was loaded in the same DbContext scope.
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var collection = await _db.Collections.FindAsync([id], ct);

        if (collection is null)
            return false;

        _db.Collections.Remove(collection);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}