using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Auth;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Users;
using Stockroom.Data;

namespace Stockroom.Api.Auth;

/// <summary>
/// Issues and rotates refresh tokens: random, opaque, and stored only as a hash (spec 10.2). Each login
/// starts a session for one device; each refresh replaces the session's current token with a new one.
/// </summary>
internal sealed partial class RefreshTokenService(StockroomDbContext db, IIdGenerator ids, TimeProvider time, ILogger<RefreshTokenService> logger)
{
    /// <summary>How long a token stays valid. Each refresh issues a new token, so a device in use stays logged in.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    // 256 random bits: far too many to guess, which is also why a fast hash is enough to store them.
    private const int TokenSizeInBytes = 32;

    /// <summary>Starts a session for one device and returns its first token.</summary>
    public async Task<string> IssueAsync(User user, string? deviceName, CancellationToken cancellationToken)
    {
        var token = Add(user.Id, ids.NewInternalId(), deviceName, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    /// <summary>
    /// Exchanges <paramref name="token"/> for its successor in the same session. Returns <c>null</c> if the
    /// token is unknown, expired, revoked, or already used. A used token means a copy is in someone else's
    /// hands (or a client replayed it), so its whole session is revoked: whoever holds the newer token
    /// has to log in again.
    /// </summary>
    public async Task<RefreshTokenRotation?> RotateAsync(string token, CancellationToken cancellationToken)
    {
        var current = await FindAsync(token, cancellationToken);
        if (current is null)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockSessionAsync(current.SessionId, cancellationToken);

        // Read the state again under the lock: a concurrent refresh or logout may have changed it.
        var state = await db.RefreshTokens
            .Where(t => t.Id == current.Id)
            .Select(t => new { t.UsedAt, t.RevokedAt })
            .SingleAsync(cancellationToken);
        var now = time.GetUtcNow();

        if (state.RevokedAt is not null || (state.UsedAt is null && current.ExpiresAt <= now))
        {
            return null;
        }

        if (state.UsedAt is not null)
        {
            await RevokeLockedSessionAsync(current.SessionId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            RefreshTokenReused(logger, current.PublicId);
            return null;
        }

        await db.RefreshTokens
            .Where(t => t.Id == current.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);
        var successor = Add(current.UserId, current.SessionId, current.DeviceName, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RefreshTokenRotation(current.UserId, successor);
    }

    /// <summary>
    /// Ends the session <paramref name="token"/> belongs to, so none of its tokens can refresh again.
    /// Does nothing for an unknown token.
    /// </summary>
    public async Task RevokeSessionAsync(string token, CancellationToken cancellationToken)
    {
        var current = await FindAsync(token, cancellationToken);
        if (current is null)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockSessionAsync(current.SessionId, cancellationToken);
        await RevokeLockedSessionAsync(current.SessionId, time.GetUtcNow(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private string Add(Guid userId, Guid sessionId, string? deviceName, DateTimeOffset now)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSizeInBytes));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = ids.NewInternalId(),
            PublicId = ids.NewPublicId(),
            UserId = userId,
            SessionId = sessionId,
            TokenHash = Hash(token),
            DeviceName = deviceName,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        });
        return token;
    }

    private Task<RefreshToken?> FindAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token);
        return db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
    }

    /// <summary>
    /// Serialises every change to one session until the transaction ends. Row locks are not enough: an
    /// UPDATE only sees rows that existed when it started, so revoking a session while a refresh was
    /// inserting its next token would miss that token.
    /// </summary>
    private Task LockSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({sessionId}::text, 0))", cancellationToken);

    // Call only inside a transaction that holds the session's lock (see LockSessionAsync).
    private Task RevokeLockedSessionAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.SessionId == sessionId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Refresh token {TokenId} was presented again after it had been used; its session has been revoked")]
    private static partial void RefreshTokenReused(ILogger logger, Guid tokenId);
}

/// <param name="UserId">Internal ID of the token's user.</param>
/// <param name="RefreshToken">The new refresh token to send to the client.</param>
internal sealed record RefreshTokenRotation(Guid UserId, string RefreshToken);
