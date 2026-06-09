namespace QuotesApi.Authorization;

/// <summary>
/// Centralised policy names.  Reference these constants everywhere;
/// never hard-code the string "can-edit-quotes" scattered through the codebase.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Required to create or update a quote (needs quotes.write scope).</summary>
    public const string CanEditQuotes = "can-edit-quotes";

    /// <summary>Required to delete a quote (needs quotes.write scope AND ownership).</summary>
    public const string CanDeleteOwnQuote = "can-delete-own-quote";
}
