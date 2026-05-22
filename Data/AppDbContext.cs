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
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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

                // SQL Server applies IDENTITY to int PK columns by convention.
                // Both columns are set explicitly — never auto-generated.
                item.Property<int>("CollectionId").ValueGeneratedNever();

                item.Property(i => i.QuoteId)
                    .IsRequired()
                    .ValueGeneratedNever();

                item.Property(i => i.AddedAt)
                    .IsRequired();

                // Shadow PK so EF can track individual rows for add/remove.
                item.HasKey("CollectionId", "QuoteId");
            });
        });

        modelBuilder.Entity<RefreshToken>(entity =>
{
    entity.HasKey(r => r.Id);
    entity.Property(r => r.Token).IsRequired();
    entity.Property(r => r.Family).IsRequired();
    entity.HasIndex(r => r.Token);   // fast lookup by token
    entity.HasIndex(r => r.Family);  // fast lookup by family for reuse detection
});
    }
}