using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Identifiers;

namespace Stockroom.Data.Auth;

/// <summary>Reads the access-token signing key, creating it on first use.</summary>
public sealed class SigningKeyStore(StockroomDbContext db, IIdGenerator ids, TimeProvider time)
{
    /// <summary>256 bits, the size HMAC-SHA256 needs.</summary>
    public const int KeySizeInBytes = 32;

    /// <summary>Returns the newest key, creating one if there is none yet.</summary>
    public async Task<byte[]> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.AcquireTransactionLockAsync(AdvisoryLocks.SigningKeyCreation, cancellationToken);

        var key = await db.SigningKeys
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => k.Key)
            .FirstOrDefaultAsync(cancellationToken);

        if (key is null)
        {
            key = RandomNumberGenerator.GetBytes(KeySizeInBytes);
            db.SigningKeys.Add(new SigningKey { Id = ids.NewInternalId(), Key = key, CreatedAt = time.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return key;
    }
}
