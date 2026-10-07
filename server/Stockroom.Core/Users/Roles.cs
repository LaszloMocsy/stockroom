using Microsoft.AspNetCore.Identity;

namespace Stockroom.Core.Users;

/// <summary>
/// The two roles (spec 2). They are seeded by the migration with fixed IDs and are not editable, so code
/// refers to them by these constants. Every user has exactly one of them.
/// </summary>
public static class Roles
{
    public const string Admin = "ADMIN";

    public const string Staff = "STAFF";

    /// <summary>Internal UUID v7 of <see cref="Admin"/>.</summary>
    public static readonly Guid AdminId = new("01a118b1-3d8a-7f81-8683-1bc68573dc24");

    /// <summary>Internal UUID v7 of <see cref="Staff"/>.</summary>
    public static readonly Guid StaffId = new("01a118b1-3d97-7474-b4be-128c287b119e");

    /// <summary>New instances of both roles, as seeded by the migration.</summary>
    public static IReadOnlyList<IdentityRole<Guid>> All() =>
    [
        Seeded(AdminId, Admin),
        Seeded(StaffId, Staff),
    ];

    // The concurrency stamp is fixed too; Identity's default is random, which would change the seed data
    // on every model build.
    private static IdentityRole<Guid> Seeded(Guid id, string name) =>
        new() { Id = id, Name = name, NormalizedName = name, ConcurrencyStamp = id.ToString() };
}
