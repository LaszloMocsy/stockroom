using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Users;

namespace Stockroom.Data.Users;

/// <summary>
/// Identity's EF Core user store, extended so that every new user gets its internal and public ID from
/// <see cref="IIdGenerator"/> (spec 3.1, "Identifiers") instead of relying on callers to set them.
/// </summary>
public sealed class StockroomUserStore(StockroomDbContext context, IIdGenerator ids, IdentityErrorDescriber? describer = null)
    : UserStore<User, IdentityRole<Guid>, StockroomDbContext, Guid>(context, describer)
{
    public override Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id == Guid.Empty)
        {
            user.Id = ids.NewInternalId();
        }

        if (user.PublicId == Guid.Empty)
        {
            user.PublicId = ids.NewPublicId();
        }

        return base.CreateAsync(user, cancellationToken);
    }
}
