using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Entities;

namespace QuotesApi.Services;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private const int TokenLifetimeDays = 7;

    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(
        AppDbContext db,
        ITokenService tokenService,
        IClock clock,
        ILogger<RefreshTokenService> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<string> IssueAsync(int userId, CancellationToken cancellationToken = default)
    {
        var rawToken = _tokenService.GenerateRefreshToken();
        var hash = HashToken(rawToken);
        var now = _clock.UtcNow;

        _db.RefreshTokens.Add(
            RefreshToken.Create(hash, userId, now.AddDays(TokenLifetimeDays), Guid.NewGuid(), now));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Refresh token issued for user {UserId}", userId);

        return rawToken;
    }

    public async Task<(string newRawToken, User user)> RotateAsync(
        string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(rawToken);

        var token = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
            throw new InvalidOperationException("Invalid refresh token.");

        if (token.IsRevoked)
        {
            // ReplacedByTokenHash is set only during rotation, not during logout.
            // Its presence means this token was already rotated into a successor — replay attack.
            if (token.ReplacedByTokenHash is not null)
            {
                _logger.LogWarning(
                    "Refresh token replay attack detected for user {UserId}. Revoking entire family {FamilyId}.",
                    token.UserId, token.FamilyId);

                await RevokeFamilyAsync(token.FamilyId, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Revoked refresh token presented for user {UserId}",
                    token.UserId);
            }

            throw new InvalidOperationException("Refresh token has been revoked.");
        }

        if (token.IsExpired)
            throw new InvalidOperationException("Refresh token has expired.");

        var newRaw = _tokenService.GenerateRefreshToken();
        var newHash = HashToken(newRaw);
        var now = _clock.UtcNow;

        // Revoke old token, recording which token replaced it (enables replay detection)
        token.Revoke(now, newHash);

        // New token inherits the same family so the entire chain can be revoked if needed
        _db.RefreshTokens.Add(
            RefreshToken.Create(newHash, token.UserId, now.AddDays(TokenLifetimeDays), token.FamilyId, now));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Refresh token rotated for user {UserId} in family {FamilyId}",
            token.UserId, token.FamilyId);

        return (newRaw, token.User);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(rawToken);
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null || token.IsRevoked)
            return; // Idempotent — already gone

        token.Revoke(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var now = _clock.UtcNow;
        foreach (var t in active)
            t.Revoke(now);

        await _db.SaveChangesAsync(cancellationToken);
    }

    // SHA-256 hex; lowercase 64-char string stored as TokenHash
    internal static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
