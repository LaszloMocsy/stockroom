using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Stockroom.Core;
using Stockroom.Core.Identifiers;

namespace Stockroom.Tests.Core;

public sealed class IdGeneratorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly IdGenerator _generator;

    public IdGeneratorTests() => _generator = new IdGenerator(_time);

    [Fact]
    public void InternalIdIsVersion7WithTheRfcVariant()
    {
        var id = _generator.NewInternalId();

        Assert.Equal(7, id.Version);
        AssertRfc9562Variant(id);
    }

    [Fact]
    public void PublicIdIsVersion4WithTheRfcVariant()
    {
        var id = _generator.NewPublicId();

        Assert.Equal(4, id.Version);
        AssertRfc9562Variant(id);
    }

    [Fact]
    public void InternalIdEmbedsTheTimeProviderTimestamp()
    {
        _time.Advance(TimeSpan.FromMilliseconds(1234));

        var id = _generator.NewInternalId();

        Assert.Equal(Start.AddMilliseconds(1234).ToUnixTimeMilliseconds(), UnixMillisecondsOf(id));
    }

    [Fact]
    public void InternalIdsSortByCreationTime()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 100; i++)
        {
            ids.Add(_generator.NewInternalId());
            _time.Advance(TimeSpan.FromMilliseconds(1));
        }

        // Guid ordering (used by .NET collections and LINQ) ...
        Assert.Equal(ids, ids.Order());
        // ... matches the canonical text and byte order that PostgreSQL uses for uuid.
        Assert.Equal(ids, ids.OrderBy(id => id.ToString(), StringComparer.Ordinal));
        Assert.Equal(ids, ids.OrderBy(id => Convert.ToHexString(id.ToByteArray(bigEndian: true)), StringComparer.Ordinal));
    }

    [Fact]
    public void InternalIdsWithinTheSameMillisecondAreUnique()
    {
        var ids = Enumerable.Range(0, 10_000).Select(_ => _generator.NewInternalId()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Equal(Start.ToUnixTimeMilliseconds(), UnixMillisecondsOf(id)));
    }

    [Fact]
    public void PublicIdsAreUniqueAndCarryNoTimestamp()
    {
        var ids = Enumerable.Range(0, 10_000).Select(_ => _generator.NewPublicId()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        // A v7-style timestamp prefix would make these cluster around Start; random ones essentially never match it.
        Assert.DoesNotContain(ids, id => UnixMillisecondsOf(id) == Start.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void AddStockroomCoreRegistersTheSystemClockAndTheGenerator()
    {
        using var provider = new ServiceCollection().AddStockroomCore().BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.IsType<IdGenerator>(provider.GetRequiredService<IIdGenerator>());
        Assert.Same(provider.GetRequiredService<IIdGenerator>(), provider.GetRequiredService<IIdGenerator>());
    }

    [Fact]
    public void AddStockroomCoreKeepsAnExistingClock()
    {
        var services = new ServiceCollection().AddSingleton<TimeProvider>(_time);
        using var provider = services.AddStockroomCore().BuildServiceProvider();

        var id = provider.GetRequiredService<IIdGenerator>().NewInternalId();

        Assert.Same(_time, provider.GetRequiredService<TimeProvider>());
        Assert.Equal(Start.ToUnixTimeMilliseconds(), UnixMillisecondsOf(id));
    }

    // RFC 9562 variant: the two most significant bits of byte 8 are 10.
    private static void AssertRfc9562Variant(Guid id) =>
        Assert.Equal(0b10, id.ToByteArray(bigEndian: true)[8] >> 6);

    // The first 48 bits of a UUID, read as an unsigned big-endian integer.
    private static long UnixMillisecondsOf(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return (long)(BinaryPrimitives.ReadUInt64BigEndian(bytes) >> 16);
    }
}
