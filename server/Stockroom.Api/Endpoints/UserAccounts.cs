using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Stockroom.Api.Errors;
using Stockroom.Core.Users;

namespace Stockroom.Api.Endpoints;

/// <summary>Creating users and reading their role, shared by setup, <c>/me</c>, and user administration.</summary>
internal static class UserAccounts
{
    /// <summary>The user's role, read from the database.</summary>
    public static async Task<string> RoleOfAsync(UserManager<User> users, User user)
    {
        // Every user has exactly one role (spec 2); anything else is a bug where the role was set.
        var roles = await users.GetRolesAsync(user);
        return roles.Count == 1
            ? roles[0]
            : throw new InvalidOperationException($"User {user.PublicId} has {roles.Count} roles instead of exactly one.");
    }

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

        EnsureSucceeded(await users.AddToRoleAsync(user, role), $"add user {user.PublicId} to {role}");
        return (user, null);
    }

    /// <summary>Throws if an Identity operation that only fails because of a bug, such as adding a role, failed.</summary>
    public static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not {operation}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
        }
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
