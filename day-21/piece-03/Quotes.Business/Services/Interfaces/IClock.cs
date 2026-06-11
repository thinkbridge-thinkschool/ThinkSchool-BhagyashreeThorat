namespace Quotes.Business.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
