using QuotesApi.Entities;

namespace QuotesApi.Abstractions;

public interface IRefreshTokenService
{
    Task<string> IssueAsync(int userId, CancellationToken cancellationToken = default);
    Task<(string newRawToken, User user)> RotateAsync(string rawToken, CancellationToken cancellationToken = default);
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);
}
