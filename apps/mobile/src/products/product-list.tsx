import { useRouter } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { StyleSheet, View } from "react-native";

import { Button } from "@/components/button";
import { TextField } from "@/components/text-field";
import { useDebouncedValue } from "@/hooks/use-debounced-value";

import { ProductPages } from "./product-pages";
import { useProducts } from "./use-products";

/** How long typing has to pause before the list is searched again. */
export const SearchDelayMs = 300;

/**
 * Every active product, sorted by name, with a search by name, SKU, or barcode (spec 4.1), and a button
 * that creates a product. More products load as the user scrolls towards the end; pulling down reloads
 * the list.
 */
export function ProductList() {
  const { t } = useTranslation();
  const router = useRouter();
  const [search, setSearch] = useState("");
  const query = useDebouncedValue(search, SearchDelayMs).trim();
  const products = useProducts({ search: query });

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
        <Button
          onPress={() => router.push("/product/new")}
          title={t("products.newProduct")}
          variant="secondary"
        />
      </View>
      <ProductPages
        emptyMessage={
          query
            ? t("products.noMatches", { search: query })
            : t("products.none")
        }
        products={products}
        testID="product-list"
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
    gap: 12,
  },
});
