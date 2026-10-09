using Microsoft.AspNetCore.Identity;

namespace Stockroom.Core.Users;

/// <summary>
/// A person who signs in to Stockroom (spec 2). Credentials, lockout, and role membership come from
/// ASP.NET Core Identity; <see cref="IdentityUser{TKey}.Id"/> is the internal UUID v7 and is never exposed.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    /// <summary>Public UUID v4, the only ID clients see.</summary>
    public Guid PublicId { get; set; }

    /// <summary>Name shown to other users, e.g. in movement history.</summary>
    public required string DisplayName { get; set; }
}
