using Microsoft.AspNetCore.Authorization;

namespace QuotesApi.Authorization;

/// <summary>
/// Marker requirement: "the authenticated user must own the quote they are trying to delete."
/// The actual logic lives in <see cref="CanDeleteOwnQuoteHandler"/>.
/// </summary>
public sealed class CanDeleteOwnQuoteRequirement : IAuthorizationRequirement { }
