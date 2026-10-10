import { unwrap } from "@stockroom/api-client";
import { useInfiniteQuery } from "@tanstack/react-query";

import { useApiClient } from "@/api/provider";

/**
 * The stock movements of the product `productId`, newest first, a page at a time: `fetchNextPage` loads
 * the next one while `hasNextPage`.
 */
export function useProductMovements(productId: string) {
  const client = useApiClient();
  return useInfiniteQuery({
    queryKey: ["movements", { product: productId }],
    queryFn: ({ pageParam, signal }) =>
      unwrap(
        client.GET("/api/v1/stock/movements", {
          params: {
            query: {
              product: productId,
              ...(pageParam !== undefined && { cursor: pageParam }),
            },
          },
          signal,
        }),
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.next_cursor ?? undefined,
  });
}
