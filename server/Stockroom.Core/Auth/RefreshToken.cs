namespace Stockroom.Core.Auth;

/// <summary>
/// A refresh token issued to one device (spec 10.2). The token itself is only ever sent to the client;
/// the server keeps its SHA-256 hash, so a database leak does not hand out working tokens.
/// </summary>
/// <remarks>
/// Tokens rotate: each refresh uses up the presented token and issues its successor in the same
/// session. Used tokens are kept, so that presenting one again is recognised as reuse.
/// </remarks>
public sealed class RefreshToken
{
    /// <summary>Internal UUID v7 primary key. Never exposed.</summary>
    public Guid Id { get; init; }

    /// <summary>Public UUID v4, the only ID clients see.</summary>
    public Guid PublicId { get; init; }

    /// <summary>Internal ID of the user the token belongs to.</summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Internal ID shared by every token in one device's chain, from login through each rotation.
    /// Revoking a session revokes all of them.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>SHA-256 of the token as the client sends it.</summary>
    public required byte[] TokenHash { get; init; }

    /// <summary>Optional label from the client, such as "Anna's iPhone", so sessions can be told apart.</summary>
    public string? DeviceName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When the token was exchanged for its successor. A used token is never accepted again.</summary>
    public DateTimeOffset? UsedAt { get; init; }

    /// <summary>When the token's session was revoked, for example because a used token was presented again.</summary>
    public DateTimeOffset? RevokedAt { get; init; }
}
