using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Users;
using Stockroom.Data;

namespace Stockroom.Api.Auth;

/// <summary>
/// Locks an account after repeated wrong passwords, for longer with each further failure (spec 11,
/// "lockout backoff"). The count of consecutive failures only resets on a successful login.
/// </summary>
/// <remarks>
/// Uses Identity's <c>access_failed_count</c> and <c>lockout_end</c> columns, but not its fixed-length
/// lockout, and applies to every user regardless of <c>lockout_enabled</c>.
/// </remarks>
internal sealed class LoginLockout(StockroomDbContext db, TimeProvider time)
{
    /// <summary>Wrong passwords allowed in a row; the next one locks the account.</summary>
    public const int FreeAttempts = 4;

    public static readonly TimeSpan FirstLockout = TimeSpan.FromMinutes(1);

    /// <summary>Upper bound, so someone guessing cannot keep a real user out for long.</summary>
    public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);

    /// <summary>How long <paramref name="user"/> stays locked out, or <c>null</c> if they may log in.</summary>
    public TimeSpan? RemainingLockout(User user)
    {
        var remaining = user.LockoutEnd - time.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    /// <summary>Lockout after <paramref name="failures"/> wrong passwords in a row: 1, 2, 4, 8, then 15 minutes.</summary>
    public static TimeSpan? LockoutAfter(int failures)
    {
        if (failures <= FreeAttempts)
        {
            return null;
        }

        var doublings = Math.Min(failures - FreeAttempts - 1, 10);
        var lockout = FirstLockout * Math.Pow(2, doublings);
        return lockout < MaxLockout ? lockout : MaxLockout;
    }

    public async Task RecordFailureAsync(User user, CancellationToken cancellationToken)
    {
        // Incremented in SQL, so concurrent guesses are all counted.
        var failures = (await db.Database
            .SqlQuery<int>($"UPDATE users SET access_failed_count = access_failed_count + 1 WHERE id = {user.Id} RETURNING access_failed_count AS \"Value\"")
            .ToListAsync(cancellationToken))
            .Single();

        if (LockoutAfter(failures) is { } lockout)
        {
            var lockoutEnd = time.GetUtcNow() + lockout;
            await db.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, lockoutEnd), cancellationToken);
        }
    }

    public async Task RecordSuccessAsync(User user, CancellationToken cancellationToken)
    {
        if (user.AccessFailedCount > 0 || user.LockoutEnd is not null)
        {
            await db.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.AccessFailedCount, 0).SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null), cancellationToken);
        }
    }
}
