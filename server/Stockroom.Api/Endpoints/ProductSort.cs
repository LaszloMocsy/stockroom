using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Products;

namespace Stockroom.Api.Endpoints;

/// <summary>The fields a product list can be sorted by.</summary>
internal enum ProductSortField
{
    Name,
    Sku,
    CreatedAt,
}

/// <summary>
/// The order of a product list, written <c>name</c>, <c>sku</c>, or <c>created_at</c>, with a leading
/// <c>-</c> for descending. Ties are broken by public ID, so the order is total and pages never overlap.
/// Text sorts in the database's collation.
/// </summary>
internal readonly record struct ProductSort(ProductSortField Field, bool Descending)
{
    public static readonly ProductSort Default = new(ProductSortField.Name, Descending: false);

    private static readonly Dictionary<string, ProductSortField> Fields = new(StringComparer.Ordinal)
    {
        ["name"] = ProductSortField.Name,
        ["sku"] = ProductSortField.Sku,
        ["created_at"] = ProductSortField.CreatedAt,
    };

    public static bool TryParse(string value, out ProductSort sort)
    {
        var descending = value.StartsWith('-');
        if (Fields.TryGetValue(descending ? value[1..] : value, out var field))
        {
            sort = new ProductSort(field, descending);
            return true;
        }

        sort = default;
        return false;
    }

    public override string ToString()
    {
        var field = Field;
        return (Descending ? "-" : "") + Fields.Single(f => f.Value == field).Key;
    }

    /// <summary>The value of the sort field for <paramref name="product"/>, as a cursor stores it.</summary>
    public string KeyOf(ProductResponse product) => Field switch
    {
        ProductSortField.Name => product.Name,
        ProductSortField.Sku => product.Sku,
        _ => product.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Whether <paramref name="key"/> could have come from <see cref="KeyOf"/>.</summary>
    public bool IsValidKey(string key) =>
        Field != ProductSortField.CreatedAt
        || (long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks <= DateTimeOffset.MaxValue.UtcTicks);

    /// <summary>
    /// <paramref name="products"/> in this order, starting after <paramref name="after"/> if given (keyset
    /// pagination). The cursor must be for this sort.
    /// </summary>
    public IQueryable<Product> Apply(IQueryable<Product> products, ProductCursor? after)
    {
        if (after is { } cursor)
        {
            var key = cursor.Key;
            var id = cursor.PublicId;
            products = (Field, Descending) switch
            {
                (ProductSortField.Name, false) => products.Where(p => EF.Functions.GreaterThan(ValueTuple.Create(p.Name, p.PublicId), ValueTuple.Create(key, id))),
                (ProductSortField.Name, true) => products.Where(p => EF.Functions.LessThan(ValueTuple.Create(p.Name, p.PublicId), ValueTuple.Create(key, id))),
                (ProductSortField.Sku, false) => products.Where(p => EF.Functions.GreaterThan(ValueTuple.Create(p.Sku, p.PublicId), ValueTuple.Create(key, id))),
                (ProductSortField.Sku, true) => products.Where(p => EF.Functions.LessThan(ValueTuple.Create(p.Sku, p.PublicId), ValueTuple.Create(key, id))),
                _ => CreatedAfter(products, new DateTimeOffset(long.Parse(key, CultureInfo.InvariantCulture), TimeSpan.Zero), id),
            };
        }

        return (Field, Descending) switch
        {
            (ProductSortField.Name, false) => products.OrderBy(p => p.Name).ThenBy(p => p.PublicId),
            (ProductSortField.Name, true) => products.OrderByDescending(p => p.Name).ThenByDescending(p => p.PublicId),
            (ProductSortField.Sku, false) => products.OrderBy(p => p.Sku).ThenBy(p => p.PublicId),
            (ProductSortField.Sku, true) => products.OrderByDescending(p => p.Sku).ThenByDescending(p => p.PublicId),
            (_, false) => products.OrderBy(p => p.CreatedAt).ThenBy(p => p.PublicId),
            (_, true) => products.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.PublicId),
        };
    }

    private IQueryable<Product> CreatedAfter(IQueryable<Product> products, DateTimeOffset createdAt, Guid id) =>
        Descending
            ? products.Where(p => EF.Functions.LessThan(ValueTuple.Create(p.CreatedAt, p.PublicId), ValueTuple.Create(createdAt, id)))
            : products.Where(p => EF.Functions.GreaterThan(ValueTuple.Create(p.CreatedAt, p.PublicId), ValueTuple.Create(createdAt, id)));
}
