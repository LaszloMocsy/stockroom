namespace Stockroom.Api.Endpoints;

/// <summary>
/// One page of a list (spec 10: cursor-based pagination). Lists that are not paged yet return everything
/// in a single page, so <paramref name="NextCursor"/> is always <c>null</c> for them.
/// </summary>
/// <param name="Items">The entries on this page.</param>
/// <param name="NextCursor">Opaque cursor for the next page, or <c>null</c> on the last page.</param>
public sealed record ListResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
