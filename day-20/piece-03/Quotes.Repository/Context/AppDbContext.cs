using Microsoft.EntityFrameworkCore;

namespace Quotes.Repository.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Author>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).IsRequired().HasMaxLength(200);

            // One author → many quotes, mapped through the _quotes backing field.
            entity.HasMany(a => a.Quotes)
                  .WithOne(q => q.AuthorRef!)
                  .HasForeignKey(q => q.AuthorId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(a => a.Quotes).HasField("_quotes");
        });

        // NOTE: EF Core automatically creates an index on the AuthorId FK. For the
        // "before" state we strip that CreateIndex out of the generated migration so
        // the slow endpoint genuinely scans Quotes on every per-author lookup. The
        // covering index is reintroduced in the dedicated "AddAuthorIdCoveringIndex"
        // migration to demonstrate the scan → seek win.

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

        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasKey(x => x.MessageId);

            entity.Property(x => x.ProcessedAtUtc)
                .IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Type)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Payload)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();
        });
    }
}
