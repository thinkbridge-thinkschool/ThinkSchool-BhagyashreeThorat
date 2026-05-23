using Microsoft.EntityFrameworkCore;
using QuotesApi.Entities;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(254);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.FamilyId);

            entity.Property(t => t.UserId).IsRequired();
            entity.Property(t => t.ExpiresAt).IsRequired();
            entity.Property(t => t.CreatedAt).IsRequired();
            entity.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);

            entity.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                  .IsRequired()
                  .HasMaxLength(80);

            entity.Property(c => c.OwnerId)
                  .IsRequired();

            // Map the private backing field so EF can populate it
            entity.Navigation(c => c.Items)
                  .HasField("_items");

            // CollectionItem as an owned type (stored in separate table)
            entity.OwnsMany(c => c.Items, item =>
            {
                item.WithOwner().HasForeignKey("CollectionId");

                item.HasKey("CollectionId", nameof(CollectionItem.QuoteId));

                item.Property(i => i.QuoteId).IsRequired().ValueGeneratedNever();
                item.Property(i => i.AddedAt).IsRequired();

                item.ToTable("CollectionItems");
            });
        });
    }
}