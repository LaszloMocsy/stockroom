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
        var hash = Hash(token);
        var now = time.GetUtcNow();
        var current = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (current is null || current.RevokedAt is not null)
        {
            return null;
        }

        if (current.UsedAt is not null)
        {
            await RevokeReusedSessionAsync(current, now, cancellationToken);
            return null;
        }

        if (current.ExpiresAt <= now)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Claim the token atomically, so of two concurrent refreshes with the same token only one succeeds.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeReusedSessionAsync(current, now, cancellationToken);
            return null;
        }

        var successor = Add(current.UserId, current.SessionId, current.DeviceName, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RefreshTokenRotation(current.UserId, successor);
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

    private async Task RevokeReusedSessionAsync(RefreshToken reused, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.RefreshTokens
            .Where(t => t.SessionId == reused.SessionId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
        RefreshTokenReused(logger, reused.PublicId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Refresh token {TokenId} was presented again after it had been used; its session has been revoked")]
    private static partial void RefreshTokenReused(ILogger logger, Guid tokenId);
}

/// <param name="UserId">Internal ID of the token's user.</param>
/// <param name="RefreshToken">The new refresh token to send to the client.</param>
internal sealed record RefreshTokenRotation(Guid UserId, string RefreshToken);
