namespace Stockroom.Core.Identifiers;

/// <summary>
/// Default <see cref="IIdGenerator"/>. Internal IDs take their timestamp from <see cref="TimeProvider"/>,
/// so tests can control it. Within one millisecond the order of internal IDs is random.
/// </summary>
public sealed class IdGenerator(TimeProvider timeProvider) : IIdGenerator
{
    public Guid NewInternalId() => Guid.CreateVersion7(timeProvider.GetUtcNow());

    // Guid.NewGuid produces a version 4 UUID from the operating system's cryptographic random source.
    public Guid NewPublicId() => Guid.NewGuid();
}
