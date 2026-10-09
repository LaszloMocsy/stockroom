using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Stock;
using Stockroom.Data;
using Stockroom.Data.Stock;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/stock/movements</c>: changing stock through the ledger (spec 3.2, 4.2), for any logged-in user.</summary>
internal static class StockMovementEndpoints
{
    public const string InsufficientStock = "insufficient_stock";
    public const string QuantityChanged = "quantity_changed";

    /// <summary>The largest quantity one movement may add, remove, or set, which keeps levels far from overflowing.</summary>
    public const int MaxQuantity = 1_000_000;

    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const int MaxIdempotencyKeyLength = 255;

    public static IEndpointRouteBuilder MapStockMovementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var movements = endpoints.MapGroup("/stock/movements").WithTags("Stock");

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
