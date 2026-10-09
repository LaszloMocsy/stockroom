using Stockroom.Core.Stock;
using Stockroom.Data;

namespace Stockroom.Api.Endpoints;

/// <summary>A stock movement, with every reference as a public ID (spec 3.1).</summary>
/// <param name="Id">The movement's public ID.</param>
/// <param name="ProductId">Public ID of the product.</param>
/// <param name="Type">What the movement did.</param>
/// <param name="Delta">Signed change applied to the quantity.</param>
/// <param name="QuantityAfter">The product's quantity right after this movement.</param>
/// <param name="Reason">Why the movement happened.</param>
/// <param name="Note">Free text.</param>
/// <param name="Reference">External reference, such as an order number or delivery note.</param>
/// <param name="VoidsMovementId">On a <c>void</c>, the public ID of the movement it reverses; otherwise <c>null</c>.</param>
/// <param name="ActorId">Public ID of the user who made the movement.</param>
/// <param name="CreatedAt">When the server recorded the movement.</param>
public sealed record StockMovementResponse(
    Guid Id,
    Guid ProductId,
    StockMovementType Type,
    int Delta,
    int QuantityAfter,
    StockMovementReason Reason,
    string? Note,
    string? Reference,
    Guid? VoidsMovementId,
    Guid ActorId,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// <paramref name="movements"/> as responses, resolving internal IDs to public ones in the database.
    /// Filter the movements before projecting: EF cannot translate a filter on the projected record.
    /// </summary>
    internal static IQueryable<StockMovementResponse> Project(IQueryable<StockMovement> movements, StockroomDbContext db) =>
        from movement in movements
        join product in db.Products on movement.ProductId equals product.Id
        join actor in db.Users on movement.ActorId equals actor.Id
        join voided in db.StockMovements on movement.VoidsMovementId equals voided.Id into voids
        from voided in voids.DefaultIfEmpty()
        select new StockMovementResponse(
            movement.PublicId,
            product.PublicId,
            movement.Type,
            movement.Delta,
            movement.QuantityAfter,
            movement.Reason,
            movement.Note,
            movement.Reference,
            voided == null ? null : voided.PublicId,
            actor.PublicId,
            movement.CreatedAt);
}
