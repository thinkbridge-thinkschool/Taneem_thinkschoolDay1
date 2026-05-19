using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote>      Quotes      => Set<Quote>();
    public DbSet<Collection> Collections => Set<Collection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                  .IsRequired()
                  .HasMaxLength(80);

            entity.Property(c => c.OwnerId)
                  .IsRequired();

            // Map the private backing field _items so EF can populate it.
            entity.Navigation(c => c.Items)
                  .HasField("_items")
                  .UsePropertyAccessMode(PropertyAccessMode.Field);

            // CollectionItem is an owned type — no separate table key needed.
            entity.OwnsMany(c => c.Items, item =>
            {
                item.WithOwner().HasForeignKey("CollectionId");

                item.Property(i => i.QuoteId)
                    .IsRequired();

                item.Property(i => i.AddedAt)
                    .IsRequired();

                // Shadow PK so EF can track individual rows for add/remove.
                item.HasKey("CollectionId", "QuoteId");
            });
        });
    }
}