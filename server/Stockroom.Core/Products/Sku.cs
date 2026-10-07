using System.Globalization;

namespace Stockroom.Core.Products;

/// <summary>The format of generated SKUs, such as <c>SR-000123</c> (spec 3.1).</summary>
public static class Sku
{
    public const string Prefix = "SR-";

    /// <summary>
    /// The SKU for a sequence number: the prefix and at least six digits, zero-padded. Numbers above
    /// 999999 simply get longer (<c>SR-1000000</c>), so generated SKUs never repeat.
    /// </summary>
    public static string FromNumber(long number)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return Prefix + number.ToString("D6", CultureInfo.InvariantCulture);
    }
}
