import { ApiResponseError, unwrap, type Schema } from "@stockroom/api-client";
import { useQueryClient } from "@tanstack/react-query";

import { useIdempotentMutation } from "@/api/idempotent-mutation";
import { useApiClient, useOptionalApiClient } from "@/api/provider";
import { productKey } from "@/products/use-products";

export type NewMovement = Schema<"CreateStockMovementRequest">;
type Product = Schema<"ProductResponse">;

/** The API's error code for removing more than is on hand. */
const InsufficientStock = "insufficient_stock";
/** The API's error code for a count whose `expected_current` is no longer the quantity. */
const QuantityChanged = "quantity_changed";

/**
 * Records a stock movement (spec 4.2) with an `Idempotency-Key`, so sending it again after a failure
 * does not record it twice. Resolves with the movement, or with undefined when a count equal to the
 * quantity recorded nothing (204).
 *
 * The product shows its new quantity at once; its lists, history, and Home's figures load again. When
 * the server refuses to remove more than is on hand, or a count because the quantity has changed, the
 * product shows the quantity the server answered with.
 */
export function useRecordMovement() {
  const client = useApiClient();
  const { setQuantity, refresh } = useStockCache();
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
      refresh();
    },
    onError: (error, movement) => {
      const current =
        insufficientStock(error)?.available ?? quantityChanged(error)?.current;
      if (current !== undefined && movement.product_id) {
        setQuantity(movement.product_id, current);
      }
    },
  });
}

/**
 * Voids the movement whose ID it is given (spec 3.2, rule 2): the server records a movement that
 * reverses it, for example to undo a quick +1 (spec 4.2). Sent with an `Idempotency-Key` too, so a
 * retry does not fail as already voided. Resolves with the reversing movement.
 *
 * The product shows its quantity after the void at once. Whether the void succeeds or fails, the
 * product, its lists and history, and Home's figures load again: a refused void may mean that someone
 * else changed or voided the movement meanwhile.
 *
 * Unlike recording, it can be used where no server is set yet, such as in a provider for the whole
 * app; voiding then fails.
 */
export function useVoidMovement() {
  const client = useOptionalApiClient();
  const { setQuantity, refresh } = useStockCache();
  return useIdempotentMutation({
    mutationFn: (movementId: string, idempotencyKey: string) => {
      if (!client) {
        return Promise.reject(new Error("No server is set."));
      }
      return unwrap(
        client.POST("/api/v1/stock/movements/{id}/void", {
          params: {
            path: { id: movementId },
            header: { "Idempotency-Key": idempotencyKey },
          },
        }),
      );
    },
    onSuccess: (movement) =>
      setQuantity(movement.product_id, movement.quantity_after),
    onSettled: refresh,
  });
}

/** Updates the cached stock after a movement. */
function useStockCache() {
  const queryClient = useQueryClient();
  return {
    /** Shows `quantity` as the product's units on hand, until it loads again. */
    setQuantity: (productId: string, quantity: number) => {
      // A load that started before the change would answer with the quantity from before it.
      void queryClient.cancelQueries({ queryKey: productKey(productId) });
      queryClient.setQueryData<Product>(
        productKey(productId),
        (product) => product && { ...product, quantity },
      );
    },
    /**
     * Loads products, movements, and Home's figures again. Not awaited: the user need not wait for
     * them, as the product already shows its quantity.
     */
    refresh: () =>
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: ["products"] }),
        queryClient.invalidateQueries({ queryKey: ["movements"] }),
        queryClient.invalidateQueries({ queryKey: ["stats"] }),
      ]),
  };
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

export interface QuantityChange {
  /** The quantity the count expected. */
  expected: number;
  /** The quantity on hand instead. */
  current: number;
}

/** The change from a `quantity_changed` error, or null for any other error. */
export function quantityChanged(error: unknown): QuantityChange | null {
  if (!(error instanceof ApiResponseError) || error.code !== QuantityChanged) {
    return null;
  }
  const { details } = error;
  return typeof details === "object" &&
    details !== null &&
    "expected" in details &&
    "current" in details &&
    typeof details.expected === "number" &&
    typeof details.current === "number"
    ? { expected: details.expected, current: details.current }
    : null;
}
