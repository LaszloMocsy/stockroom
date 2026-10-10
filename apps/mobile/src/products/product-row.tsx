import type { Schema } from "@stockroom/api-client";
import { Link } from "expo-router";
import { useTranslation } from "react-i18next";
import { Pressable, StyleSheet, Text } from "react-native";

import { StockBadge } from "./stock-badge";
import { stockStatus } from "./stock-status";

/**
 * A product's name, SKU, units on hand, and minimum, with a badge in words and an icon when it is low on
 * or out of stock. Opens the product.
 */
export function ProductRow({
  product,
}: {
  product: Schema<"ProductResponse">;
}) {
  const { t } = useTranslation();
  return (
    <Link
      asChild
      href={{ pathname: "/product/[id]", params: { id: product.id } }}
    >
      <Pressable
        role="link"
        style={({ pressed }) => [styles.row, pressed && styles.pressed]}
      >
        <Text style={styles.name}>{product.name}</Text>
        <Text style={styles.sku}>{product.sku}</Text>
        <Text style={styles.text}>
          {product.min_stock === null
            ? t("productRow.quantity", { quantity: product.quantity })
            : t("productRow.quantityAndMinimum", {
                quantity: product.quantity,
                minStock: product.min_stock,
              })}
        </Text>
        <StockBadge status={stockStatus(product)} />
      </Pressable>
    </Link>
  );
}

const styles = StyleSheet.create({
  row: {
    minHeight: 48,
    gap: 4,
    paddingVertical: 8,
  },
  pressed: {
    opacity: 0.6,
  },
  name: {
    fontSize: 16,
    fontWeight: "600",
  },
  sku: {
    fontSize: 14,
  },
  text: {
    fontSize: 16,
  },
});
