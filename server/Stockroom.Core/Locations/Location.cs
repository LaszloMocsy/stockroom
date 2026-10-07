namespace Stockroom.Core.Locations;

/// <summary>
/// A place where stock is kept (spec 3.1, "Location"). Until multi-location support (P1) there is only
/// the built-in <see cref="MainStorage"/>, but stock levels and movements already reference a location,
/// so adding more is not a breaking change.
/// </summary>
public sealed class Location
{
    /// <summary>Internal UUID v7 of the built-in default location. Fixed, because the migration seeds it.</summary>
    public static readonly Guid MainStorageId = new("01a1188a-b06f-7fcb-8303-25d87cf9124e");

    /// <summary>Public UUID v4 of the built-in default location.</summary>
    public static readonly Guid MainStoragePublicId = new("84e27bfb-10a7-4d7a-856b-453cca2bc434");

    public const string MainStorageName = "Main storage";

    /// <summary>Internal UUID v7 primary key. Never exposed.</summary>
    public Guid Id { get; init; }

    /// <summary>Public UUID v4, the only ID clients see.</summary>
    public Guid PublicId { get; init; }

    public required string Name { get; set; }

    /// <summary>A new instance of the built-in default location, as seeded by the migration.</summary>
    public static Location MainStorage() =>
        new() { Id = MainStorageId, PublicId = MainStoragePublicId, Name = MainStorageName };
}
