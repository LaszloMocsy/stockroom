import type { Schema } from "@stockroom/api-client";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  FlatList,
  RefreshControl,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { Button } from "@/components/button";
import { TextField } from "@/components/text-field";
import { useDebouncedValue } from "@/hooks/use-debounced-value";

import { useProducts } from "./use-products";

/** How long typing has to pause before the list is searched again. */
export const SearchDelayMs = 300;

/**
 * Every active product, sorted by name, with a search by name, SKU, or barcode (spec 4.1). More
 * products load as the user scrolls towards the end; pulling down reloads the list.
 */
export function ProductList() {
  const { t } = useTranslation();
  const [search, setSearch] = useState("");
  const query = useDebouncedValue(search, SearchDelayMs);
  const products = useProducts(query);
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
    <View style={styles.screen}>
      <View style={styles.search}>
        <TextField
          autoCapitalize="none"
          autoCorrect={false}
          clearButtonMode="while-editing"
          enterKeyHint="search"
          label={t("products.searchLabel")}
          onChangeText={setSearch}
          placeholder={t("products.searchPlaceholder")}
          value={search}
        />
      </View>
      <FlatList
        contentContainerStyle={styles.list}
        data={items}
        keyboardDismissMode="on-drag"
        keyboardShouldPersistTaps="handled"
        keyExtractor={(product) => product.id}
        ListEmptyComponent={
          products.data ? (
            <Text style={styles.text}>
              {query.trim()
                ? t("products.noMatches", { search: query.trim() })
                : t("products.none")}
            </Text>
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
        renderItem={({ item }) => <ProductRow product={item} />}
        testID="product-list"
      />
    </View>
  );
}

/** A product's name, SKU, and quantity, with low stock in words rather than colour. */
function ProductRow({ product }: { product: Schema<"ProductResponse"> }) {
  const { t } = useTranslation();
  const lowStock =
    product.min_stock !== null && product.quantity <= product.min_stock;
  return (
    // One element for screen readers.
    <View accessible style={styles.product}>
      <Text style={styles.productName}>{product.name}</Text>
      <Text style={styles.productSku}>{product.sku}</Text>
      <Text style={styles.text}>
        {product.quantity <= 0
          ? t("products.outOfStock")
          : lowStock
            ? t("products.quantityLow", { quantity: product.quantity })
            : t("products.quantity", { quantity: product.quantity })}
      </Text>
    </View>
  );
}

function LoadError({
  message,
  retry,
  retrying,
}: {
  message: string;
  retry: () => void;
  retrying: boolean;
}) {
  const { t } = useTranslation();
  return (
    <View style={styles.error}>
      <Text role="alert" style={[styles.text, styles.errorText]}>
        {message}
      </Text>
      <Button
        busy={retrying}
        onPress={retry}
        title={retrying ? t("products.retrying") : t("products.retry")}
        variant="secondary"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
  },
  search: {
    paddingHorizontal: 24,
    paddingTop: 16,
    paddingBottom: 8,
  },
  list: {
    gap: 16,
    paddingHorizontal: 24,
    paddingVertical: 16,
  },
  product: {
    gap: 2,
  },
  productName: {
    fontSize: 16,
    fontWeight: "600",
  },
  productSku: {
    fontSize: 14,
  },
  text: {
    fontSize: 16,
  },
  error: {
    gap: 12,
  },
  errorText: {
    color: "#b3261e",
  },
});
