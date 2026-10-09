using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

/// <summary>Builders for data-layer tests that need a user, e.g. as the actor of a movement.</summary>
internal static class TestUsers
{
    /// <summary>A user without a password, saved directly rather than through Identity's <c>UserManager</c>.</summary>
    public static User New(string userName, Guid? publicId = null) =>
        new()
        {
            Id = TestDatabase.Ids.NewInternalId(),
            PublicId = publicId ?? TestDatabase.Ids.NewPublicId(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            DisplayName = "User " + userName,
        };
}
