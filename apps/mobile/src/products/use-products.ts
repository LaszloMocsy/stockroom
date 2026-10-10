import { unwrap } from "@stockroom/api-client";
import { keepPreviousData, useInfiniteQuery } from "@tanstack/react-query";

import { useApiClient } from "@/api/provider";

/**
 * The active products matching `search` (their name, SKU, or a barcode contains it), sorted by name, a
 * page at a time: `fetchNextPage` loads the next one while `hasNextPage`. Blank text matches every
 * product. While the results for new text load, the previous ones stay, so the list does not flash
 * empty between keystrokes.
 */
export function useProducts(search: string) {
  const client = useApiClient();
  const q = search.trim() || undefined;
  return useInfiniteQuery({
    queryKey: ["products", "list", { q }],
    queryFn: ({ pageParam, signal }) =>
      unwrap(
        client.GET("/api/v1/products", {
          params: {
            query: {
              ...(q !== undefined && { q }),
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
