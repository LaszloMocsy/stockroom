using System.Buffers.Text;
using System.Text;

namespace Stockroom.Api.Endpoints;

/// <summary>
/// Where a page of products ended: the sort it was read in, and the last product's sort key and public ID.
/// Clients see it only as an opaque string; it holds no internal ID.
/// </summary>
internal readonly record struct ProductCursor(ProductSort Sort, string Key, Guid PublicId)
{
    // The key goes last because it is free text, which may itself contain the separator.
    public string Encode() => Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{PublicId:N}.{Sort}.{Key}"));

    /// <summary>
    /// Reads a cursor from <see cref="Encode"/> that was made for <paramref name="sort"/>;
    /// <see langword="false"/> for anything else.
    /// </summary>
    public static bool TryDecode(string value, ProductSort sort, out ProductCursor cursor)
    {
        cursor = default;
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(Base64Url.DecodeFromChars(value));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }

        var parts = text.Split('.', 3);
        if (parts.Length != 3
            || !Guid.TryParseExact(parts[0], "N", out var publicId)
            || parts[1] != sort.ToString()
            || !sort.IsValidKey(parts[2]))
        {
            return false;
        }

        cursor = new ProductCursor(sort, parts[2], publicId);
        return true;
    }
}
