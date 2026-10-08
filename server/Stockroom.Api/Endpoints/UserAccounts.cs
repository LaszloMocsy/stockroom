using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Stockroom.Api.Errors;
using Stockroom.Core.Users;

namespace Stockroom.Api.Endpoints;

/// <summary>Creating users, shared by first-run setup and user administration.</summary>
internal static class UserAccounts
{
    /// <summary>
    /// Creates a user with exactly one role. Username and password problems come back as a
    /// <c>validation_failed</c> error instead of a user. Call it inside a transaction, so a user is never
    /// left without a role.
    /// </summary>
    public static async Task<(User? User, ProblemHttpResult? Error)> CreateAsync(
        UserManager<User> users, string username, string displayName, string password, string role)
    {
        var user = new User { UserName = username, DisplayName = displayName };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return (null, ValidationFailed(created));
        }

        var addedToRole = await users.AddToRoleAsync(user, role);
        if (!addedToRole.Succeeded)
        {
            throw new InvalidOperationException($"Could not add user {user.PublicId} to {role}: {string.Join(" ", addedToRole.Errors.Select(e => e.Description))}");
        }

        return (user, null);
    }

    /// <summary>
    /// A <c>validation_failed</c> error for a failed Identity operation. Identity reports username and
    /// password problems with codes such as <c>PasswordTooShort</c> and <c>DuplicateUserName</c>.
    /// </summary>
    public static ProblemHttpResult ValidationFailed(IdentityResult result) =>
        ApiResults.ValidationFailed(result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "username")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}
