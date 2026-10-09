using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Stockroom.Api.Endpoints;

/// <summary>
/// Where a page of movements ended: the last movement's <c>created_at</c> and public ID, the keys the list
/// is sorted by. Clients see it only as an opaque string; it holds no internal ID.
/// </summary>
internal readonly record struct StockMovementCursor(DateTimeOffset CreatedAt, Guid PublicId)
{
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{CreatedAt.UtcTicks}.{PublicId:N}")));

    /// <summary>Reads a cursor from <see cref="Encode"/>; <see langword="false"/> for anything else.</summary>
    public static bool TryDecode(string value, out StockMovementCursor cursor)
    {
        cursor = default;
        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(bytes).Split('.');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(parts[1], "N", out var publicId))
        {
            return false;
        }

        cursor = new StockMovementCursor(new DateTimeOffset(ticks, TimeSpan.Zero), publicId);
        return true;
    }
}
