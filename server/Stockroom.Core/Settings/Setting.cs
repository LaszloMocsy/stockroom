namespace Stockroom.Core.Settings;

/// <summary>
/// A runtime-changeable preference stored in the database (spec 12.2), as opposed to the
/// <c>STOCKROOM_*</c> environment variables fixed at startup. A setting that was never written has its
/// <see cref="Default"/>.
/// </summary>
/// <param name="Key">The stored name, in snake_case.</param>
public sealed record Setting<T>(string Key, T Default);
