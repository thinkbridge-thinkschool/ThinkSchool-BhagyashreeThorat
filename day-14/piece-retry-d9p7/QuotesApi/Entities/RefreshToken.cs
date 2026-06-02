namespace QuotesApi.Entities;

public class RefreshToken
{
    public int Id { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public int UserId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid FamilyId { get; private set; }

    public User User { get; private set; } = null!;

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;

    private RefreshToken() { }

    public static RefreshToken Create(
        string tokenHash,
        int userId,
        DateTimeOffset expiresAt,
        Guid familyId,
        DateTimeOffset createdAt) =>
        new()
        {
            TokenHash = tokenHash,
            UserId = userId,
            ExpiresAt = expiresAt,
            FamilyId = familyId,
            CreatedAt = createdAt
        };

    public void Revoke(DateTimeOffset now, string? replacedByTokenHash = null)
    {
        RevokedAt = now;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
