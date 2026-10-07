using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Stockroom.Data.Conversions;

/// <summary>
/// Stores an enum as its lowercase snake_case name (<c>TransferOut</c> becomes <c>transfer_out</c>), the
/// spelling the specification and the API use. Text keeps the column readable and lets new members be
/// added without a schema change.
/// </summary>
internal sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(v => ToName(v), v => FromName(v))
    where TEnum : struct, Enum
{
    private static readonly FrozenDictionary<TEnum, string> Names =
        Enum.GetValues<TEnum>().ToFrozenDictionary(v => v, v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()));

    private static readonly FrozenDictionary<string, TEnum> Values =
        Names.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    private static string ToName(TEnum value) => Names[value];

    private static TEnum FromName(string name) =>
        Values.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"'{name}' is not a known {typeof(TEnum).Name} value.");
}
