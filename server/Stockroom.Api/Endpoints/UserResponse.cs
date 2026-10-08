using Microsoft.AspNetCore.Identity;
using Stockroom.Core.Users;

namespace Stockroom.Api.Endpoints;

/// <param name="Id">The user's public ID (UUID v4).</param>
/// <param name="Username">Login name, unique ignoring case.</param>
/// <param name="DisplayName">Name shown in the apps, e.g. on stock movements.</param>
/// <param name="Role"><c>ADMIN</c> or <c>STAFF</c>.</param>
public sealed record UserResponse(Guid Id, string Username, string DisplayName, string Role)
{
    internal static UserResponse From(User user, string role) =>
        new(user.PublicId, user.UserName!, user.DisplayName, role);

    /// <summary>The response for <paramref name="user"/>, with the role read from the database.</summary>
    internal static async Task<UserResponse> FromAsync(User user, UserManager<User> users)
    {
        // Every user has exactly one role (spec 2); anything else is a bug where the role was set.
        var roles = await users.GetRolesAsync(user);
        return roles.Count == 1
            ? From(user, roles[0])
            : throw new InvalidOperationException($"User {user.PublicId} has {roles.Count} roles instead of exactly one.");
    }
}
