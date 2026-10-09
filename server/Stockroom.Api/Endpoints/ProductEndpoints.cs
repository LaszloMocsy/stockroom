using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Products;
using Stockroom.Data;
using Stockroom.Data.Stock;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/products</c>: the catalogue (spec 4.1). Any logged-in user can read and create products.</summary>
internal static class ProductEndpoints
{
    public const string SkuTaken = "sku_taken";
    public const string BarcodeTaken = "barcode_taken";

    // The unique indexes behind SkuTaken and BarcodeTaken, named by the migrations.
    private const string SkuIndex = "ix_products_sku";
    private const string BarcodeIndex = "ix_product_barcodes_barcode";

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var products = endpoints.MapGroup("/products").WithTags("Products");

        products.MapPost("", CreateProductAsync)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithName("CreateProduct")
            .WithSummary("Create a product")
            .WithDescription(string.Create(
                CultureInfo.InvariantCulture,
                $"""
                Creates a product and returns it. Without a `sku` the server generates one such as `SR-000123`. An `initial_quantity` above 0 is recorded as an `initial` movement in the same transaction, so the product never exists without its starting stock.

                A `sku` that another product has returns 409 with `{SkuTaken}`, and a `barcode` that another product has returns 409 with `{BarcodeTaken}`; `details.product_id` is that product's ID.
                """));

        products.MapGet("/{id:guid}", GetProductAsync)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithName("GetProduct")
            .WithSummary("Get a product")
            .WithDescription("Returns the product `id` with its barcodes and the units on hand. Archived products are returned too; `archived_at` tells them apart.");
        return endpoints;
    }

    private static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> GetProductAsync(
        [Description("Public ID of the product.")] Guid id,
        StockroomDbContext db,
        CancellationToken cancellationToken)
    {
        var product = await ProductResponse.Project(db.Products.Where(p => p.PublicId == id), db).SingleOrDefaultAsync(cancellationToken);
        return product is null
            ? ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "There is no product with this ID.")
            : TypedResults.Ok(product);
    }

    private static async Task<Results<Created<ProductResponse>, ProblemHttpResult>> CreateProductAsync(
        CreateProductRequest request,
        ClaimsPrincipal principal,
        StockroomDbContext db,
        StockService stock,
        ISkuGenerator skus,
        IIdGenerator ids,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var actorPublicId = principal.UserPublicId();
        var actorId = await db.Users.Where(u => u.PublicId == actorPublicId).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
        if (actorId is null)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "The user for this access token no longer exists.");
        }

        // The product, its barcode, and its initial movement are created together or not at all.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = time.GetUtcNow();
        var product = new Product
        {
            Id = ids.NewInternalId(),
            PublicId = ids.NewPublicId(),
            Sku = request.Sku ?? await NewSkuAsync(db, skus, cancellationToken),
            Name = request.Name,
            Description = request.Description,
            MinStock = request.MinStock,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };
        if (request.Barcode is not null)
        {
            product.Barcodes.Add(new ProductBarcode { Id = ids.NewInternalId(), ProductId = product.Id, Barcode = request.Barcode });
        }

        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique
            && unique.ConstraintName is SkuIndex or BarcodeIndex)
        {
            // The failed insert aborted the transaction; the owner is looked up outside it.
            await transaction.RollbackAsync(cancellationToken);
            return unique.ConstraintName == SkuIndex
                ? ApiResults.Error(
                    StatusCodes.Status409Conflict,
                    SkuTaken,
                    $"Another product already has the SKU {product.Sku}.",
                    new { product.Sku, ProductId = await OwnerAsync(db.Products.Where(p => p.Sku == product.Sku), cancellationToken) })
                : ApiResults.Error(
                    StatusCodes.Status409Conflict,
                    BarcodeTaken,
                    $"Another product already has the barcode {request.Barcode}.",
                    new
                    {
                        request.Barcode,
                        ProductId = await OwnerAsync(
                            db.Products.Where(p => p.Barcodes.Any(b => b.Barcode == request.Barcode)),
                            cancellationToken),
                    });
        }

        if (request.InitialQuantity is > 0)
        {
            await stock.RecordInitialAsync(new InitialStock(product.Id, request.InitialQuantity.Value, actorId.Value), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var response = await ProductResponse.Project(db.Products.Where(p => p.Id == product.Id), db).SingleAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/products/{response.Id}", response);
    }

    /// <summary>
    /// A generated SKU that no product has. The sequence never repeats itself, but someone may have typed
    /// a SKU in its format by hand, so those numbers are skipped.
    /// </summary>
    private static async Task<string> NewSkuAsync(StockroomDbContext db, ISkuGenerator skus, CancellationToken cancellationToken)
    {
        string sku;
        do
        {
            sku = await skus.NextAsync(cancellationToken);
        }
        while (await db.Products.AnyAsync(p => p.Sku == sku, cancellationToken));

        return sku;
    }

    /// <summary>
    /// The public ID of the product in <paramref name="owners"/>, or <see langword="null"/> if it was created
    /// by a transaction that has since rolled back.
    /// </summary>
    private static Task<Guid?> OwnerAsync(IQueryable<Product> owners, CancellationToken cancellationToken) =>
        owners.Select(p => (Guid?)p.PublicId).SingleOrDefaultAsync(cancellationToken);

    /// <summary>Blank text, which DataAnnotations would only catch by also making optional fields required.</summary>
    private static ProblemHttpResult? Validate(CreateProductRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["The name cannot be blank."];
        }

        if (request.Sku is not null && string.IsNullOrWhiteSpace(request.Sku))
        {
            errors[nameof(request.Sku)] = ["The SKU cannot be blank. Omit it to have one generated."];
        }

        if (request.Barcode is not null && string.IsNullOrWhiteSpace(request.Barcode))
        {
            errors[nameof(request.Barcode)] = ["The barcode cannot be blank."];
        }

        return errors.Count == 0 ? null : ApiResults.ValidationFailed(errors);
    }
}

/// <summary>Request body for <c>POST /api/v1/products</c>.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Sku">Human-readable unique ID. Generated (e.g. <c>SR-000123</c>) when omitted; cannot be changed later.</param>
/// <param name="Description">Free text.</param>
/// <param name="MinStock">Low-stock threshold; omit for no alert.</param>
/// <param name="Barcode">A scanned payload to attach, stored exactly as given.</param>
/// <param name="InitialQuantity">Units on hand to start with, recorded as an <c>initial</c> movement. Omit or 0 for none.</param>
public sealed record CreateProductRequest(
    [property: Required, StringLength(200)] string Name,
    [property: StringLength(64)] string? Sku = null,
    [property: StringLength(2000)] string? Description = null,
    [property: Range(0, StockMovementEndpoints.MaxQuantity)] int? MinStock = null,
    // Long enough for QR payloads, short enough for the unique index (PostgreSQL limits B-tree entries to about 2.7 kB).
    [property: StringLength(512)] string? Barcode = null,
    [property: Range(0, StockMovementEndpoints.MaxQuantity)] int? InitialQuantity = null);
