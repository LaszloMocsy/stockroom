import { ApiResponseError, unwrap, type Schema } from "@stockroom/api-client";
import { useQueryClient } from "@tanstack/react-query";

import { useIdempotentMutation } from "@/api/idempotent-mutation";
import { useApiClient } from "@/api/provider";
import { productKey } from "@/products/use-products";

export type NewMovement = Schema<"CreateStockMovementRequest">;
type Product = Schema<"ProductResponse">;

/** The API's error code for removing more than is on hand. */
const InsufficientStock = "insufficient_stock";

/**
 * Records a stock movement (spec 4.2) with an `Idempotency-Key`, so sending it again after a failure
 * does not record it twice. Resolves with the movement, or with undefined when a count equal to the
 * quantity recorded nothing (204).
 *
 * The product shows its new quantity at once; its lists, history, and Home's figures load again. When
 * the server refuses to remove more than is on hand, the product shows what is on hand by then.
 */
export function useRecordMovement() {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const setQuantity = (productId: string, quantity: number) =>
    queryClient.setQueryData<Product>(
      productKey(productId),
      (product) => product && { ...product, quantity },
    );

  return useIdempotentMutation({
    mutationFn: (movement: NewMovement, idempotencyKey: string) =>
      unwrap(
        client.POST("/api/v1/stock/movements", {
          params: { header: { "Idempotency-Key": idempotencyKey } },
          body: movement,
        }),
      ),
    onSuccess: (movement) => {
      if (movement) {
        setQuantity(movement.product_id, movement.quantity_after);
      }
      // Not awaited: the user need not wait for them, as the product already shows its quantity.
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: ["products"] }),
        queryClient.invalidateQueries({ queryKey: ["movements"] }),
        queryClient.invalidateQueries({ queryKey: ["stats"] }),
      ]);
    },
    onError: (error, movement) => {
      const shortage = insufficientStock(error);
      if (shortage && movement.product_id) {
        setQuantity(movement.product_id, shortage.available);
      }
    },
  });
}

export interface Shortage {
  /** The units the movement would have removed. */
  requested: number;
  /** The units on hand. */
  available: number;
}

/** The shortage from an `insufficient_stock` error, or null for any other error. */
export function insufficientStock(error: unknown): Shortage | null {
  if (
    !(error instanceof ApiResponseError) ||
    error.code !== InsufficientStock
  ) {
    return null;
  }
  const { details } = error;
  return typeof details === "object" &&
    details !== null &&
    "requested" in details &&
    "available" in details &&
    typeof details.requested === "number" &&
    typeof details.available === "number"
    ? { requested: details.requested, available: details.available }
    : null;
}
