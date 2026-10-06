namespace Stockroom.Core.Identifiers;

/// <summary>
/// Creates entity identifiers (spec 3.1, "Identifiers"). Every API-visible entity has both:
/// an internal ID used as primary and foreign key, and a public ID that is the only one clients see.
/// </summary>
public interface IIdGenerator
{
    /// <summary>
    /// A UUID v7 for an internal <c>id</c>. Its leading 48 bits are the Unix time in milliseconds,
    /// so IDs created in different milliseconds sort in creation order (as <see cref="Guid"/> and in
    /// PostgreSQL), which keeps primary-key inserts index-friendly. Never expose it outside the server.
    /// </summary>
    Guid NewInternalId();

    /// <summary>
    /// A UUID v4 for a <c>public_id</c>: 122 random bits from a cryptographically secure source, so it
    /// reveals nothing about creation time or volume and cannot be guessed.
    /// </summary>
    Guid NewPublicId();
}
