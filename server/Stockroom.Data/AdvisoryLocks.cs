using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Stockroom.Data;

/// <summary>
/// PostgreSQL advisory locks held until the current transaction ends. Every fixed key is listed here, so
/// two features cannot pick the same one by accident.
/// </summary>
/// <remarks>
/// Fixed keys are 8 ASCII characters read as a number. Keys derived from an ID or a text key are a 64-bit
/// hash of it, which collides with another key only by a 1 in 2^64 chance.
/// </remarks>
public static class AdvisoryLocks
{
    /// <summary>Serialises creating users, so concurrent requests cannot both pass the "username is free" or "no users yet" check. "NewUser!".</summary>
    public const long UserCreation = 0x4e65775573657221;

    /// <summary>Serialises demotions, so two requests cannot each demote one of the last two ADMINs. "AdminLck".</summary>
    public const long AdminDemotion = 0x41646d696e4c636b;

    /// <summary>Serialises creating the first signing key, so concurrent callers cannot each create one. "SignKey!".</summary>
    public const long SigningKeyCreation = 0x5369676e4b657921;

    /// <summary>Waits for and takes the lock <paramref name="key"/>. Call it inside a transaction.</summary>
    public static Task AcquireTransactionLockAsync(this DatabaseFacade database, long key, CancellationToken cancellationToken) =>
        database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);

    /// <summary>Waits for and takes the lock for one entity, such as a session. Call it inside a transaction.</summary>
    public static Task AcquireTransactionLockAsync(this DatabaseFacade database, Guid id, CancellationToken cancellationToken) =>
        database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id}::text, 0))", cancellationToken);

    /// <summary>
    /// Waits for and takes the lock for a text key, such as an actor's idempotency key. Start the key with
    /// what it identifies (e.g. <c>idempotency:</c>), so it cannot equal another feature's key or an ID.
    /// Call it inside a transaction.
    /// </summary>
    public static Task AcquireTransactionLockAsync(this DatabaseFacade database, string key, CancellationToken cancellationToken) =>
        database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
}
