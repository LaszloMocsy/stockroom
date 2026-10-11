import type { Schema } from "@stockroom/api-client";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  FlatList,
  RefreshControl,
  StyleSheet,
  Text,
} from "react-native";

import { LoadError } from "@/components/load-error";

import { ProductRow } from "./product-row";
import type { useProducts } from "./use-products";

export interface ProductPagesProps {
  products: ReturnType<typeof useProducts>;
  /** Shown when the list has no products. */
  emptyMessage: string;
  /** Called with the product the user taps, instead of opening it. */
  onPick?: (product: Schema<"ProductResponse">) => void;
  testID?: string;
}

/**
 * Products from `useProducts`, loading the next page as the user scrolls towards the end. Pulling down
 * reloads them. Each opens the product, or is picked with `onPick`.
 */
export function ProductPages({
  products,
  emptyMessage,
  onPick,
  testID,
}: ProductPagesProps) {
  const { t } = useTranslation();
  const [refreshing, setRefreshing] = useState(false);

  const items = products.data?.pages.flatMap((page) => page.items) ?? [];

  const loadMore = () => {
    if (
      products.hasNextPage &&
      !products.isFetchingNextPage &&
      !products.isFetchNextPageError
    ) {
      void products.fetchNextPage();
    }
  };

  const refresh = async () => {
    setRefreshing(true);
    await products.refetch();
    setRefreshing(false);
  };

  return (
    <FlatList
      contentContainerStyle={styles.list}
      data={items}
      keyboardDismissMode="on-drag"
      keyboardShouldPersistTaps="handled"
      keyExtractor={(product) => product.id}
      ListEmptyComponent={
        products.data ? (
          <Text style={styles.text}>{emptyMessage}</Text>
        ) : products.error ? (
          <LoadError
            message={t("products.loadFailed")}
            retry={() => void products.refetch()}
            retrying={products.isFetching}
          />
        ) : (
          <ActivityIndicator />
        )
      }
      ListFooterComponent={
        products.isFetchNextPageError ? (
          <LoadError
            message={t("products.loadMoreFailed")}
            retry={() => void products.fetchNextPage()}
            retrying={products.isFetchingNextPage}
          />
        ) : products.isFetchingNextPage ? (
          <ActivityIndicator />
        ) : null
      }
      onEndReached={loadMore}
      onEndReachedThreshold={0.5}
      refreshControl={
        <RefreshControl
          onRefresh={() => void refresh()}
          refreshing={refreshing}
        />
      }
      renderItem={({ item }) => <ProductRow onPress={onPick} product={item} />}
      testID={testID}
    />
  );
}

const styles = StyleSheet.create({
  list: {
    paddingHorizontal: 24,
    paddingVertical: 8,
  },
  text: {
    fontSize: 16,
  },
});
