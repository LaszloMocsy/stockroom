namespace Stockroom.Core.Auth;

/// <summary>
/// A refresh token issued to one device at login (spec 10.2). The token itself is only ever sent to the
/// client; the server keeps its SHA-256 hash, so a database leak does not hand out working tokens.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>Internal UUID v7 primary key. Never exposed.</summary>
    public Guid Id { get; init; }

    /// <summary>Public UUID v4, the only ID clients see.</summary>
    public Guid PublicId { get; init; }

    /// <summary>Internal ID of the user the token belongs to.</summary>
    public Guid UserId { get; init; }

    /// <summary>SHA-256 of the token as the client sends it.</summary>
    public required byte[] TokenHash { get; init; }

    /// <summary>Optional label from the client, such as "Anna's iPhone", so sessions can be told apart.</summary>
    public string? DeviceName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}
