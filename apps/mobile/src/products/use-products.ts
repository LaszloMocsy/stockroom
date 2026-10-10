import { unwrap } from "@stockroom/api-client";
import {
  keepPreviousData,
  useInfiniteQuery,
  useQuery,
} from "@tanstack/react-query";

import { useApiClient } from "@/api/provider";

export interface ProductFilter {
  /** Text to find in the name, SKU, or a barcode; blank matches every product. */
  search?: string;
  /** Only products at or below their `min_stock`. */
  lowStock?: boolean;
}

/**
 * The active products matching `filter`, sorted by name, a page at a time: `fetchNextPage` loads the
 * next one while `hasNextPage`. While the results for a new filter load, the previous ones stay, so a
 * search does not flash empty between keystrokes.
 */
export function useProducts({ search = "", lowStock = false }: ProductFilter) {
  const client = useApiClient();
  const q = search.trim() || undefined;
  return useInfiniteQuery({
    queryKey: ["products", "list", { q, lowStock }],
    queryFn: ({ pageParam, signal }) =>
      unwrap(
        client.GET("/api/v1/products", {
          params: {
            query: {
              ...(q !== undefined && { q }),
              ...(lowStock && { low_stock: true }),
              ...(pageParam !== undefined && { cursor: pageParam }),
            },
          },
          signal,
        }),
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.next_cursor ?? undefined,
    placeholderData: keepPreviousData,
  });
}

/** The product `id`, with its barcodes and units on hand, archived or not. */
export function useProduct(id: string) {
  const client = useApiClient();
  return useQuery({
    queryKey: ["products", "detail", id],
    queryFn: ({ signal }) =>
      unwrap(
        client.GET("/api/v1/products/{id}", {
          params: { path: { id } },
          signal,
        }),
      ),
  });
}
