import type { Schema } from "@stockroom/api-client";

/**
 * How a product's stock stands (spec 4.3): `out` with none on hand (or fewer, where negative stock is
 * allowed), `low` at or below its `min_stock`, and `ok` otherwise, including without a `min_stock`.
 */
export type StockStatus = "out" | "low" | "ok";

export function stockStatus(
  product: Pick<Schema<"ProductResponse">, "quantity" | "min_stock">,
): StockStatus {
  if (product.quantity <= 0) {
    return "out";
  }
  if (product.min_stock !== null && product.quantity <= product.min_stock) {
    return "low";
  }
  return "ok";
}
