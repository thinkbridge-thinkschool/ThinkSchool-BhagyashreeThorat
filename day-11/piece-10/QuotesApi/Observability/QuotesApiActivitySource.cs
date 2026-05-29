using System.Diagnostics;

namespace QuotesApi.Observability;

internal static class QuotesApiActivitySource
{
    internal const string Name = "QuotesApi";
    internal static readonly ActivitySource Source = new(Name, "1.0.0");
}
