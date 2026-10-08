namespace Stockroom.Data.Auth;

/// <summary>
/// Key that signs access tokens. The server generates it on first start (see <see cref="SigningKeyStore"/>),
/// so a self-hosted install needs no secret in its configuration.
/// </summary>
public sealed class SigningKey
{
    /// <summary>Internal UUID v7 primary key. Never exposed.</summary>
    public Guid Id { get; init; }

    public required byte[] Key { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
