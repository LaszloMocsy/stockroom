using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Stock;
using Stockroom.Data;
using Stockroom.Data.Stock;

namespace Stockroom.Api.Endpoints;

/// <summary>
/// <c>/api/v1/stock/movements</c>: the ledger (spec 3.2, 4.2). Any logged-in user can read it and change stock
/// through it.
/// </summary>
internal static class StockMovementEndpoints
{
    public const string InsufficientStock = "insufficient_stock";
    public const string QuantityChanged = "quantity_changed";

    /// <summary>The largest quantity one movement may add, remove, or set, which keeps levels far from overflowing.</summary>
    public const int MaxQuantity = 1_000_000;

    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const int MaxIdempotencyKeyLength = 255;

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapStockMovementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var movements = endpoints.MapGroup("/stock/movements").WithTags("Stock");

        movements.MapGet("", ListMovementsAsync)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithName("ListStockMovements")
            .WithSummary("List stock movements")
            .WithDescription(string.Create(
                CultureInfo.InvariantCulture,
                $"""
                Returns movements newest first, optionally filtered; all filters combine. An unknown `product` or `actor` matches nothing. Pages hold up to `limit` movements ({DefaultPageSize} by default, at most {MaxPageSize}); pass `next_cursor` back as `cursor` with the same filters for the next page, until it is `null`.
                """));

        movements.MapPost("", CreateMovementAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithName("CreateStockMovement")
            .WithSummary("Add, remove, or count stock")
            .WithDescription(string.Create(
                CultureInfo.InvariantCulture,
                $"""
                Records one movement and returns it. `receive` adds `quantity` units and `issue` removes them; `adjust` sets the quantity to the counted `target_quantity` and records the difference. Quantities are whole numbers up to {MaxQuantity:N0}. `reason` defaults to `purchase`, `sale`, and `count` respectively.

                An `issue` of more than is on hand returns 409 with `{InsufficientStock}` and `details.requested` and `details.available`, unless negative stock is allowed. An `adjust` with an `expected_current` that is no longer the quantity returns 409 with `{QuantityChanged}` and `details.expected` and `details.current`, so the user can confirm against the new value. An `adjust` to the current quantity records nothing and returns 204.

                Send an `{IdempotencyKeyHeader}` header (up to {MaxIdempotencyKeyLength} characters, unique per user) to make retries safe: repeating a request with a key that already recorded a movement returns that movement again and changes nothing.
                """));
        return endpoints;
    }

    private static async Task<Results<Ok<ListResponse<StockMovementResponse>>, ProblemHttpResult>> ListMovementsAsync(
        [Description("Public ID of the product.")] Guid? product,
        [Description("Movement type: `receive`, `issue`, `adjust`, `initial`, or `void`.")] string? type,
        [Description("Public ID of the user who made the movement.")] Guid? actor,
        [Description("Earliest `created_at` to include, as an ISO 8601 timestamp.")] DateTimeOffset? from,
        [Description("Exclusive end: only movements created before this ISO 8601 timestamp.")] DateTimeOffset? to,
        [Description("`next_cursor` from the previous page.")] string? cursor,
        [Description("Page size.")] int? limit,
        StockroomDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var after = default(StockMovementCursor?);
        if (cursor is not null)
        {
            if (StockMovementCursor.TryDecode(cursor, out var decoded))
            {
                after = decoded;
            }
            else
            {
                errors[nameof(cursor)] = ["The cursor is not one this API returned."];
            }
        }

        // Bound as text: the query-string binder would want the C# name ("Issue") and also accept numbers.
        var movementType = default(StockMovementType?);
        if (type is not null)
        {
            movementType = Enum.GetValues<StockMovementType>()
                .Select(t => (StockMovementType?)t)
                .SingleOrDefault(t => JsonNamingPolicy.SnakeCaseLower.ConvertName(t.ToString()!) == type);
            if (movementType is null)
            {
                errors[nameof(type)] = ["The type must be receive, issue, adjust, initial, or void."];
            }
        }

        if (limit is < 1 or > MaxPageSize)
        {
            errors[nameof(limit)] = [$"The limit must be between 1 and {MaxPageSize}."];
        }

        if (from is not null && to is not null && to <= from)
        {
            errors[nameof(to)] = ["The end must be after the start."];
        }

        if (errors.Count > 0)
        {
            return ApiResults.ValidationFailed(errors);
        }

        var movements = db.StockMovements.AsQueryable();
        if (product is not null)
        {
            var productId = await db.Products.Where(p => p.PublicId == product).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(cancellationToken);
            movements = movements.Where(m => m.ProductId == productId);
        }

        if (actor is not null)
        {
            var actorId = await db.Users.Where(u => u.PublicId == actor).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
            movements = movements.Where(m => m.ActorId == actorId);
        }

        if (movementType is not null)
        {
            movements = movements.Where(m => m.Type == movementType);
        }

        // PostgreSQL stores timestamps in UTC, and Npgsql only accepts UTC values for them.
        if (from is not null)
        {
            var start = from.Value.ToUniversalTime();
            movements = movements.Where(m => m.CreatedAt >= start);
        }

        if (to is not null)
        {
            var end = to.Value.ToUniversalTime();
            movements = movements.Where(m => m.CreatedAt < end);
        }

        // Keyset pagination: everything sorted after the previous page's last movement.
        if (after is { } last)
        {
            movements = movements.Where(m => EF.Functions.LessThan(
                ValueTuple.Create(m.CreatedAt, m.PublicId),
                ValueTuple.Create(last.CreatedAt, last.PublicId)));
        }

        // One extra row tells whether another page follows.
        var pageSize = limit ?? DefaultPageSize;
        var items = await StockMovementResponse.Project(movements, db).Take(pageSize + 1).ToListAsync(cancellationToken);
        var nextCursor = items.Count > pageSize
            ? new StockMovementCursor(items[pageSize - 1].CreatedAt, items[pageSize - 1].Id).Encode()
            : null;

        return TypedResults.Ok(new ListResponse<StockMovementResponse>(items.Take(pageSize).ToList(), nextCursor));
    }

    private static async Task<Results<Created<StockMovementResponse>, NoContent, ProblemHttpResult>> CreateMovementAsync(
        CreateStockMovementRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        ClaimsPrincipal principal,
        StockroomDbContext db,
        StockService stock,
        CancellationToken cancellationToken)
    {
        if (Validate(request, idempotencyKey) is { } invalid)
        {
            return invalid;
        }

        var actorPublicId = principal.UserPublicId();
        var actorId = await db.Users.Where(u => u.PublicId == actorPublicId).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
        if (actorId is null)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "The user for this access token no longer exists.");
        }

        var productId = await db.Products.Where(p => p.PublicId == request.ProductId).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(cancellationToken);
        if (productId is null)
        {
            return ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "There is no product with this ID.");
        }

        StockMovement? movement;
        try
        {
            movement = request.Type switch
            {
                NewStockMovementType.Receive => await stock.ReceiveAsync(
                    new ReceiveStock(productId.Value, request.Quantity!.Value, actorId.Value)
                    {
                        Reason = request.Reason ?? StockMovementReason.Purchase,
                        Note = request.Note,
                        Reference = request.Reference,
                        IdempotencyKey = idempotencyKey,
                    },
                    cancellationToken),
                NewStockMovementType.Issue => await stock.IssueAsync(
                    new IssueStock(productId.Value, request.Quantity!.Value, actorId.Value)
                    {
                        Reason = request.Reason ?? StockMovementReason.Sale,
                        Note = request.Note,
                        Reference = request.Reference,
                        IdempotencyKey = idempotencyKey,
                    },
                    cancellationToken),
                _ => await stock.AdjustAsync(
                    new AdjustStock(productId.Value, request.TargetQuantity!.Value, actorId.Value)
                    {
                        ExpectedCurrent = request.ExpectedCurrent,
                        Reason = request.Reason ?? StockMovementReason.Count,
                        Note = request.Note,
                        Reference = request.Reference,
                        IdempotencyKey = idempotencyKey,
                    },
                    cancellationToken),
            };
        }
        catch (InsufficientStockException ex)
        {
            return ApiResults.Error(
                StatusCodes.Status409Conflict,
                InsufficientStock,
                $"Only {ex.Available} units are on hand, so {ex.Requested} cannot be removed.",
                new { ex.Requested, ex.Available });
        }
        catch (StockConflictException ex)
        {
            return ApiResults.Error(
                StatusCodes.Status409Conflict,
                QuantityChanged,
                $"The quantity changed from {ex.Expected} to {ex.Current} since it was read. Confirm the count against the new value.",
                new { ex.Expected, ex.Current });
        }

        if (movement is null)
        {
            return TypedResults.NoContent();
        }

        // Read back rather than built from the request: a retried key returns the movement it first recorded.
        var response = await StockMovementResponse
            .Project(db.StockMovements.Where(m => m.Id == movement.Id), db)
            .SingleAsync(cancellationToken);

        // No Location header: movements are read through the list endpoint, not one at a time.
        return TypedResults.Created((string?)null, response);
    }

    /// <summary>The checks that depend on the movement type, which DataAnnotations cannot express.</summary>
    private static ProblemHttpResult? Validate(CreateStockMovementRequest request, string? idempotencyKey)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Type is NewStockMovementType.Adjust)
        {
            if (request.TargetQuantity is null)
            {
                errors[nameof(request.TargetQuantity)] = ["An adjust needs the counted quantity."];
            }

            if (request.Quantity is not null)
            {
                errors[nameof(request.Quantity)] = ["An adjust takes target_quantity, not quantity."];
            }
        }
        else
        {
            if (request.Quantity is null)
            {
                errors[nameof(request.Quantity)] = ["The number of units to move is required."];
            }

            if (request.TargetQuantity is not null)
            {
                errors[nameof(request.TargetQuantity)] = ["Only an adjust takes target_quantity."];
            }

            if (request.ExpectedCurrent is not null)
            {
                errors[nameof(request.ExpectedCurrent)] = ["Only an adjust takes expected_current."];
            }
        }

        if (idempotencyKey is not null && (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength))
        {
            errors[IdempotencyKeyHeader] = [$"The key must be 1 to {MaxIdempotencyKeyLength} characters and not blank."];
        }

        return errors.Count == 0 ? null : ApiResults.ValidationFailed(errors);
    }
}

/// <summary>The movement types a client can record directly; <c>initial</c> and <c>void</c> come from other endpoints.</summary>
public enum NewStockMovementType
{
    Receive,
    Issue,
    Adjust,
}

/// <summary>Request body for <c>POST /api/v1/stock/movements</c>.</summary>
/// <param name="ProductId">Public ID of the product.</param>
/// <param name="Type"><c>receive</c>, <c>issue</c>, or <c>adjust</c>.</param>
/// <param name="Quantity">For <c>receive</c> and <c>issue</c>: the whole number of units to add or remove.</param>
/// <param name="TargetQuantity">For <c>adjust</c>: the counted quantity to set.</param>
/// <param name="ExpectedCurrent">For <c>adjust</c>, optional: the quantity the user saw before counting. If it has changed since, nothing is recorded and the response is 409.</param>
/// <param name="Reason">Why stock moved. Defaults to <c>purchase</c> for <c>receive</c>, <c>sale</c> for <c>issue</c>, and <c>count</c> for <c>adjust</c>.</param>
/// <param name="Note">Free text.</param>
/// <param name="Reference">External reference, such as an order number or delivery note.</param>
public sealed record CreateStockMovementRequest(
    [property: Required] Guid? ProductId,
    [property: Required] NewStockMovementType? Type,
    [property: Range(1, StockMovementEndpoints.MaxQuantity)] int? Quantity = null,
    [property: Range(0, StockMovementEndpoints.MaxQuantity)] int? TargetQuantity = null,
    int? ExpectedCurrent = null,
    StockMovementReason? Reason = null,
    [property: StringLength(2000)] string? Note = null,
    [property: StringLength(200)] string? Reference = null);
