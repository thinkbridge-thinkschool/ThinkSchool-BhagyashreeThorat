using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Options;

public sealed record DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    [Required]
    public string Default { get; init; } = "";
}
