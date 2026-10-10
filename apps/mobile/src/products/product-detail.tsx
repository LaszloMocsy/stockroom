import { ApiResponseError, type Schema } from "@stockroom/api-client";
import { useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { LoadError } from "@/components/load-error";

import { StockBadge } from "./stock-badge";
import { stockStatus } from "./stock-status";
import { useProduct } from "./use-products";

/**
 * A product (spec 3.1): its name, SKU, barcodes, units on hand, and minimum, with a badge when it is low
 * on or out of stock. Pulling down reloads it.
 */
export function ProductDetail({ id }: { id: string }) {
  const { t } = useTranslation();
  const product = useProduct(id);
  const [refreshing, setRefreshing] = useState(false);

  const refresh = async () => {
    setRefreshing(true);
    await product.refetch();
    setRefreshing(false);
  };

  let content;
  if (product.data) {
    content = <Details product={product.data} />;
  } else if (
    product.error instanceof ApiResponseError &&
    product.error.status === 404
  ) {
    content = (
      <Text role="alert" style={styles.text}>
        {t("product.notFound")}
      </Text>
    );
  } else if (product.error) {
    content = (
      <LoadError
        message={t("product.loadFailed")}
        retry={() => void product.refetch()}
        retrying={product.isFetching}
      />
    );
  } else {
    content = <ActivityIndicator />;
  }

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      contentInsetAdjustmentBehavior="automatic"
      refreshControl={
        <RefreshControl
          onRefresh={() => void refresh()}
          refreshing={refreshing}
        />
      }
      testID="product"
    >
      {content}
    </ScrollView>
  );
}

function Details({ product }: { product: Schema<"ProductResponse"> }) {
  const { t } = useTranslation();
  return (
    <>
      <View style={styles.section}>
        <Text role="heading" style={styles.name}>
          {product.name}
        </Text>
        {product.archived_at && (
          <Text style={styles.text}>{t("product.archived")}</Text>
        )}
      </View>
      {/* One element for screen readers, read as the number, "on hand", and the badge. */}
      <View accessible style={styles.section}>
        <Text style={styles.quantity}>
          {t("product.number", { value: product.quantity })}
        </Text>
        <Text style={styles.label}>{t("product.onHand")}</Text>
        <StockBadge status={stockStatus(product)} />
      </View>
      <Field label={t("product.sku")}>
        <Text style={styles.text}>{product.sku}</Text>
      </Field>
      <Field label={t("product.minStock")}>
        <Text style={styles.text}>
          {product.min_stock === null
            ? t("product.noMinStock")
            : t("product.number", { value: product.min_stock })}
        </Text>
      </Field>
      <Field label={t("product.barcodes")}>
        {product.barcodes.length === 0 ? (
          <Text style={styles.text}>{t("product.noBarcodes")}</Text>
        ) : (
          product.barcodes.map((barcode) => (
            <Text key={barcode} style={styles.text}>
              {barcode}
            </Text>
          ))
        )}
      </Field>
      {product.description && (
        <Field label={t("product.description")}>
          <Text style={styles.text}>{product.description}</Text>
        </Field>
      )}
    </>
  );
}

/** A labelled value, read by screen readers as one element. */
function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <View accessible style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 24,
    padding: 24,
  },
  section: {
    gap: 8,
  },
  field: {
    gap: 4,
  },
  name: {
    fontSize: 24,
    fontWeight: "600",
  },
  quantity: {
    fontSize: 40,
    fontWeight: "600",
  },
  label: {
    fontSize: 14,
    fontWeight: "600",
  },
  text: {
    fontSize: 16,
  },
});
