using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Stockroom.Core.Auth;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Users;
using Stockroom.Data;

namespace Stockroom.Api.Auth;

/// <summary>Issues refresh tokens: random, opaque, and stored only as a hash (spec 10.2).</summary>
internal sealed class RefreshTokenIssuer(StockroomDbContext db, IIdGenerator ids, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    // 256 random bits: far too many to guess, which is also why a fast hash is enough to store them.
    private const int TokenSizeInBytes = 32;

    /// <summary>Creates and stores a refresh token for one device, and returns the token to send to it.</summary>
    public async Task<string> IssueAsync(User user, string? deviceName, CancellationToken cancellationToken)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSizeInBytes));
        var now = time.GetUtcNow();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = ids.NewInternalId(),
            PublicId = ids.NewPublicId(),
            UserId = user.Id,
            TokenHash = Hash(token),
            DeviceName = deviceName,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        return token;
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
