using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Entities;
using QuotesApi.Observability;
using QuotesApi.Options;

namespace QuotesApi.Services;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;
    private readonly ILogger<RefreshTokenService> _logger;
    private readonly JwtOptions _jwt;

    public RefreshTokenService(
        AppDbContext db,
        ITokenService tokenService,
        IClock clock,
        ILogger<RefreshTokenService> logger,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _tokenService = tokenService;
        _clock = clock;
        _logger = logger;
        _jwt = jwtOptions.Value;
    }

    public async Task<string> IssueAsync(int userId, CancellationToken cancellationToken = default)
    {
        var rawToken = _tokenService.GenerateRefreshToken();
        var hash = HashToken(rawToken);
        var now = _clock.UtcNow;

        _db.RefreshTokens.Add(
            RefreshToken.Create(hash, userId, now.AddDays(_jwt.RefreshTokenLifetimeDays), Guid.NewGuid(), now));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Refresh token issued for user {UserId}", userId);

        return rawToken;
    }

    public async Task<(string newRawToken, User user)> RotateAsync(
        string rawToken, CancellationToken cancellationToken = default)
    {
        using var activity = QuotesApiActivitySource.Source.StartActivity("token.rotate");

        var hash = HashToken(rawToken);

        var token = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Token not found");
            throw new InvalidOperationException("Invalid refresh token.");
        }

        // Safe metadata only — no token hashes, no secrets.
        activity?.SetTag("token.userId", token.UserId);
        activity?.SetTag("token.familyId", token.FamilyId.ToString());

        if (token.IsRevoked)
        {
            // ReplacedByTokenHash is set only during rotation, not during logout.
            // Its presence means this token was already rotated into a successor — replay attack.
            if (token.ReplacedByTokenHash is not null)
            {
                activity?.SetTag("token.replayAttack", true);
                activity?.SetStatus(ActivityStatusCode.Error, "Replay attack — entire token family revoked");

                _logger.LogWarning(
                    "Refresh token replay attack detected for user {UserId}. Revoking entire family {FamilyId}.",
                    token.UserId, token.FamilyId);

                await RevokeFamilyAsync(token.FamilyId, cancellationToken);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Revoked token reused");

                _logger.LogWarning(
                    "Revoked refresh token presented for user {UserId}",
                    token.UserId);
            }

            throw new InvalidOperationException("Refresh token has been revoked.");
        }

        if (token.IsExpired)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Token expired");
            throw new InvalidOperationException("Refresh token has expired.");
        }

        var newRaw = _tokenService.GenerateRefreshToken();
        var newHash = HashToken(newRaw);
        var now = _clock.UtcNow;

        // Revoke old token, recording which token replaced it (enables replay detection)
        token.Revoke(now, newHash);

        // New token inherits the same family so the entire chain can be revoked if needed
        _db.RefreshTokens.Add(
            RefreshToken.Create(newHash, token.UserId, now.AddDays(_jwt.RefreshTokenLifetimeDays), token.FamilyId, now));

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
