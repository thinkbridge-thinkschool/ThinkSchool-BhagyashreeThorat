using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Entities;

namespace Tests.Domain.Infrastructure;

/// <summary>
/// Helpers that write seed data directly into the test database,
/// bypassing the HTTP layer.  Used when a test needs pre-existing records
/// (e.g. "seed a quote owned by user X, then try to delete it as user Y").
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Creates a user directly in the DB.  Idempotent: returns the existing
    /// user's ID if the email is already registered.
    /// </summary>
    public static async Task<int> SeedUserAsync(
        IServiceProvider services,
        string           email,
        string           password = "Password123!")
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant());
        if (existing is not null)
            return existing.Id;

        var user = User.Create(email, BCrypt.Net.BCrypt.HashPassword(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// Inserts a quote directly into the DB and returns its generated ID.
    /// </summary>
    public static async Task<int> SeedQuoteAsync(
        IServiceProvider services,
        string           author,
        string           text,
        int?             ownerId = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = Quote.Create(author, text, ownerId);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"DbSeeder: Quote.Create failed — {result.Error}");

        db.Quotes.Add(result.Value!);
        await db.SaveChangesAsync();
        return result.Value!.Id;
    }

    /// <summary>Returns the DB-assigned ID of an already-seeded user.</summary>
    public static async Task<int> GetUserIdAsync(IServiceProvider services, string email)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Users.FirstAsync(u => u.Email == email.ToLowerInvariant())).Id;
    }

    /// <summary>
    /// Directly back-dates a refresh token's ExpiresAt so that
    /// IsExpired returns true, letting tests exercise the expiry path
    /// without sleeping.
    /// </summary>
    public static async Task ExpireRefreshTokenAsync(
        IServiceProvider services,
        string           rawToken)
    {
        using var scope = services.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = QuotesApi.Services.RefreshTokenService.HashToken(rawToken);
        var past = DateTimeOffset.UtcNow.AddDays(-1);

        await db.Database.ExecuteSqlAsync(
            $"UPDATE RefreshTokens SET ExpiresAt = {past} WHERE TokenHash = {hash}");
    }
}
