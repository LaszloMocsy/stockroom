using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Stockroom.Api.Auth;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user's public ID, from the access token's <c>sub</c> claim.</summary>
    public static Guid? UserPublicId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
