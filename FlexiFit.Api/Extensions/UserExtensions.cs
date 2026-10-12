using System.Security.Claims;

namespace FlexiFit.Api.Extensions;

/// <summary>
/// Extension methods for ClaimsPrincipal to easily extract user information.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extracts the User ID from JWT Claims safely.
    /// Checks both standard NameIdentifier and custom "user_id" claims.
    /// </summary>
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                user.FindFirst("user_id")?.Value;

        return int.TryParse(id, out var userId) ? userId : null;
    }
}
