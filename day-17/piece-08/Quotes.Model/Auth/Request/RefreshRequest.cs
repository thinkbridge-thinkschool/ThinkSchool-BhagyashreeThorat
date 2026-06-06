using System.ComponentModel.DataAnnotations;

namespace Quotes.Model.Auth;

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
