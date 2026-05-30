namespace QuotesApi.Options;

public sealed record KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    public string? Uri { get; init; }
}
