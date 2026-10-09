using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Products;
using Stockroom.Data;
using Stockroom.Data.Stock;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/products</c>: the catalogue (spec 4.1). Any logged-in user can read, create, and edit products; archiving and restoring them is for ADMIN users.</summary>
internal static class ProductEndpoints
{
    public const string SkuTaken = "sku_taken";
    public const string BarcodeTaken = "barcode_taken";

    // The unique indexes behind SkuTaken and BarcodeTaken, named by the migrations.
    private const string SkuIndex = "ix_products_sku";
    private const string BarcodeIndex = "ix_product_barcodes_barcode";

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 200;

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var products = endpoints.MapGroup("/products").WithTags("Products");

        products.MapGet("", ListProductsAsync)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithName("ListProducts")
            .WithSummary("List products")
            .WithDescription(string.Create(
                CultureInfo.InvariantCulture,
                $"""
                Returns products matching `q`, if given: those whose name, SKU, or any barcode contains it, ignoring case. A blank `q` matches everything.

                Archived products are left out unless `archived` is `true`, which lists only them. `low_stock=true` keeps only products at or below their `min_stock`, and `low_stock=false` only the others, including those without a `min_stock`. All filters combine.

                Products are sorted by `sort`: `name` (the default), `sku`, or `created_at`, with a leading `-` for descending, e.g. `-created_at` for newest first. Products that tie are ordered by ID. Pages hold up to `limit` products ({DefaultPageSize} by default, at most {MaxPageSize}); pass `next_cursor` back as `cursor` with the same filters and `sort` for the next page, until it is `null`.
                """));

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

        products.MapPatch("/{id:guid}", UpdateProductAsync)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithName("UpdateProduct")
            .WithSummary("Update a product")
            .WithDescription("""
                Changes the product's name, description, or `min_stock` and returns it. Fields that are omitted stay as they are; `description` and `min_stock` can be set to `null` to remove them. `updated_at` changes only if a value does.

                The SKU never changes: a `sku` field, like any field products do not have, is a validation error. Quantities change only through stock movements.
                """);

        products.MapPost("/{id:guid}/archive", ArchiveProductAsync)
            .RequireAdmin()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithName("ArchiveProduct")
            .WithSummary("Archive a product")
            .WithDescription("Archives the product `id` and returns it. ADMIN only. Archived products keep their stock and history but are left out of the product list unless it asks for them. Archiving an archived product changes nothing, so its `archived_at` stays the time it was first archived.");

        products.MapPost("/{id:guid}/restore", RestoreProductAsync)
            .RequireAdmin()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithName("RestoreProduct")
            .WithSummary("Restore an archived product")
            .WithDescription("Makes the archived product `id` active again and returns it. ADMIN only. Restoring an active product changes nothing.");
        return endpoints;
    }

    private static async Task<Results<Ok<ListResponse<ProductResponse>>, ProblemHttpResult>> ListProductsAsync(
        [Description("Text to find in the name, SKU, or a barcode.")] string? q,
        [Description("`true` for only products at or below their `min_stock`, `false` for only the others.")]
        [FromQuery(Name = "low_stock")]
        bool? lowStock,
        [Description("`true` for only archived products; `false`, the default, for only active ones.")] bool? archived,
        [Description("`name`, `sku`, or `created_at`; a leading `-` sorts descending.")] string? sort,
        [Description("`next_cursor` from the previous page.")] string? cursor,
        [Description("Page size.")] int? limit,
        StockroomDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (q?.Length > MaxSearchLength)
        {
            errors[nameof(q)] = [$"The search text can be at most {MaxSearchLength} characters."];
        }

        var order = ProductSort.Default;
        if (sort is not null && !ProductSort.TryParse(sort, out order))
        {
            errors[nameof(sort)] = ["The sort must be name, sku, or created_at, optionally with a leading -."];
        }

        var after = default(ProductCursor?);
        if (cursor is not null && errors.Count == 0)
        {
            if (ProductCursor.TryDecode(cursor, order, out var decoded))
            {
                after = decoded;
            }
            else
            {
                errors[nameof(cursor)] = ["The cursor is not one this API returned for this sort."];
            }
        }

        if (limit is < 1 or > MaxPageSize)
        {
            errors[nameof(limit)] = [$"The limit must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            return ApiResults.ValidationFailed(errors);
        }

        var products = db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            // ILIKE with the trigram indexes; the text is matched literally, so % and _ are escaped.
            var pattern = "%" + EscapeLikePattern(q.Trim()) + "%";
            products = products.Where(p => EF.Functions.ILike(p.Name, pattern, LikeEscape)
                || EF.Functions.ILike(p.Sku, pattern, LikeEscape)
                || p.Barcodes.Any(b => EF.Functions.ILike(b.Barcode, pattern, LikeEscape)));
        }

        products = archived == true ? products.Where(p => p.ArchivedAt != null) : products.Where(p => p.ArchivedAt == null);

        // Low stock is a quantity, summed as ProductResponse does, at or below min_stock (spec 4.3). A product
        // without a min_stock is never low.
        if (lowStock == true)
        {
            products = products.Where(p => p.MinStock != null
                && (db.StockLevels.Where(l => l.ProductId == p.Id).Sum(l => (int?)l.Quantity) ?? 0) <= p.MinStock);
        }
        else if (lowStock == false)
        {
            products = products.Where(p => p.MinStock == null
                || (db.StockLevels.Where(l => l.ProductId == p.Id).Sum(l => (int?)l.Quantity) ?? 0) > p.MinStock);
        }

        // One extra row tells whether another page follows.
        var pageSize = limit ?? DefaultPageSize;
        var items = await ProductResponse.Project(order.Apply(products, after), db).Take(pageSize + 1).ToListAsync(cancellationToken);
        var nextCursor = items.Count > pageSize
            ? new ProductCursor(order, order.KeyOf(items[pageSize - 1]), items[pageSize - 1].Id).Encode()
            : null;

        return TypedResults.Ok(new ListResponse<ProductResponse>(items.Take(pageSize).ToList(), nextCursor));
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

    private static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> UpdateProductAsync(
        [Description("Public ID of the product.")] Guid id,
        UpdateProductRequest request,
        StockroomDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var product = await db.Products.SingleOrDefaultAsync(p => p.PublicId == id, cancellationToken);
        if (product is null)
        {
            return ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "There is no product with this ID.");
        }

        if (request.HasName)
        {
            product.Name = request.Name!;
        }

        if (request.HasDescription)
        {
            product.Description = request.Description;
        }

        if (request.HasMinStock)
        {
            product.MinStock = request.MinStock;
        }

        // EF tracks a value set to what it already was as unchanged.
        if (db.ChangeTracker.HasChanges())
        {
            product.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await ProductResponse.Project(db.Products.Where(p => p.Id == product.Id), db).SingleAsync(cancellationToken));
    }

    private static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> ArchiveProductAsync(
        [Description("Public ID of the product.")] Guid id,
        StockroomDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // Guarded, so a repeated or concurrent archive keeps the first archived_at.
        var now = time.GetUtcNow();
        await db.Products
            .Where(p => p.PublicId == id && p.ArchivedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.ArchivedAt, now), cancellationToken);
        return await GetProductAsync(id, db, cancellationToken);
    }

    private static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> RestoreProductAsync(
        [Description("Public ID of the product.")] Guid id,
        StockroomDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Products
            .Where(p => p.PublicId == id && p.ArchivedAt != null)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.ArchivedAt, (DateTimeOffset?)null), cancellationToken);
        return await GetProductAsync(id, db, cancellationToken);
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

    private const string LikeEscape = "\\";

    private static string EscapeLikePattern(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

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

    /// <summary>What DataAnnotations cannot express: a name that is removed or blank, and fields products do not have.</summary>
    private static ProblemHttpResult? Validate(UpdateProductRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.HasName && string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["The name cannot be blank or removed."];
        }

        foreach (var field in request.UnknownFields?.Keys ?? Enumerable.Empty<string>())
        {
            // Reported under the name as sent; the envelope's snake_case conversion leaves snake_case names alone.
            errors[field] = field == "sku"
                ? ["The SKU cannot be changed."]
                : ["Products have no such field."];
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

/// <summary>
/// Request body for <c>PATCH /api/v1/products/{id}</c>, a JSON merge patch (RFC 7396): fields that are omitted
/// stay as they are, and <c>null</c> removes a value. The <c>Has…</c> flags tell an omitted field from a <c>null</c> one.
/// </summary>
public sealed class UpdateProductRequest
{
    private readonly string? _name;
    private readonly string? _description;
    private readonly int? _minStock;

    /// <summary>New display name. Cannot be <c>null</c> or blank.</summary>
    [StringLength(200)]
    public string? Name
    {
        get => _name;
        init => (_name, HasName) = (value, true);
    }

    /// <summary>New free-text description; <c>null</c> removes it.</summary>
    [StringLength(2000)]
    public string? Description
    {
        get => _description;
        init => (_description, HasDescription) = (value, true);
    }

    /// <summary>New low-stock threshold; <c>null</c> turns the alert off.</summary>
    [Range(0, StockMovementEndpoints.MaxQuantity)]
    public int? MinStock
    {
        get => _minStock;
        init => (_minStock, HasMinStock) = (value, true);
    }

    [JsonIgnore]
    public bool HasName { get; private init; }

    [JsonIgnore]
    public bool HasDescription { get; private init; }

    [JsonIgnore]
    public bool HasMinStock { get; private init; }

    /// <summary>Fields products do not have, such as <c>sku</c>, which are rejected rather than ignored.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}
