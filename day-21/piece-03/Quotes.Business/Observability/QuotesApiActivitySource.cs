using System.Diagnostics;

namespace Quotes.Business.Observability;

public static class QuotesApiActivitySource
{
    public const string Name = "QuotesApi";
    public static readonly ActivitySource Source = new(Name, "1.0.0");
}
