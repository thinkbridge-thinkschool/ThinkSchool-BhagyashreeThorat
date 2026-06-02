using QuotesApi.Entities;

namespace QuotesApi.Abstractions;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}
