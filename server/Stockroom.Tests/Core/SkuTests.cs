using Stockroom.Core.Products;

namespace Stockroom.Tests.Core;

public sealed class SkuTests
{
    [Theory]
    [InlineData(1, "SR-000001")]
    [InlineData(123, "SR-000123")]
    [InlineData(999_999, "SR-999999")]
    [InlineData(1_000_000, "SR-1000000")]
    public void NumbersArePaddedToSixDigits(long number, string expected) =>
        Assert.Equal(expected, Sku.FromNumber(number));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NumbersStartAtOne(long number) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Sku.FromNumber(number));
}
