import { unwrap, type Schema } from "@stockroom/api-client";
import { useQueryClient } from "@tanstack/react-query";

import { useIdempotentMutation } from "@/api/idempotent-mutation";
import { useApiClient } from "@/api/provider";
import { productKey } from "@/products/use-products";

export type NewMovement = Schema<"CreateStockMovementRequest">;
type Product = Schema<"ProductResponse">;

/**
 * Records a stock movement (spec 4.2) with an `Idempotency-Key`, so sending it again after a failure
 * does not record it twice. Resolves with the movement, or with undefined when a count equal to the
 * quantity recorded nothing (204).
 *
 * The product shows its new quantity at once; its lists, history, and Home's figures load again.
 */
export function useRecordMovement() {
  const client = useApiClient();
  const queryClient = useQueryClient();
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
        queryClient.setQueryData<Product>(
          productKey(movement.product_id),
          (product) =>
            product && { ...product, quantity: movement.quantity_after },
        );
      }
      // Not awaited: the user need not wait for them, as the product already shows its quantity.
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: ["products"] }),
        queryClient.invalidateQueries({ queryKey: ["movements"] }),
        queryClient.invalidateQueries({ queryKey: ["stats"] }),
      ]);
    },
  });
}
