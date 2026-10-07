namespace Stockroom.Core.Products;

/// <summary>Creates SKUs for products created without one (spec 4.1).</summary>
public interface ISkuGenerator
{
    /// <summary>
    /// A SKU no earlier call has returned, even under concurrency. It may still match a SKU someone
    /// typed in by hand; the unique index on <c>products.sku</c> rejects that case.
    /// </summary>
    Task<string> NextAsync(CancellationToken cancellationToken);
}
