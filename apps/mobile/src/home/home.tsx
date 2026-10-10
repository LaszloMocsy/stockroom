import { unwrap, type Schema } from "@stockroom/api-client";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { useApiClient } from "@/api/provider";
import { Button } from "@/components/button";
import { LoadError } from "@/components/load-error";
import { ProductRow } from "@/products/product-row";

/** How many low-stock products Home shows; the low-stock list has them all. */
export const LowStockPreviewSize = 5;

/**
 * The dashboard (spec 7.1): how many products and units there are, how many are low on stock or out of
 * stock, and the first few low-stock products. Pulling down reloads it; like any query, it also reloads
 * when the app returns to the foreground.
 */
export function Home() {
  const { t } = useTranslation();
  const client = useApiClient();
  const summary = useQuery({
    queryKey: ["stats", "summary"],
    queryFn: () => unwrap(client.GET("/api/v1/stats/summary")),
  });
  const lowStock = useQuery({
    queryKey: ["products", { lowStock: true, limit: LowStockPreviewSize }],
    queryFn: () =>
      unwrap(
        client.GET("/api/v1/products", {
          params: {
            query: { low_stock: true, limit: LowStockPreviewSize },
          },
        }),
      ),
  });
  const [refreshing, setRefreshing] = useState(false);

  const refresh = async () => {
    setRefreshing(true);
    await Promise.all([summary.refetch(), lowStock.refetch()]);
    setRefreshing(false);
  };

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
      testID="home"
    >
      <View style={styles.section}>
        <Text role="heading" style={styles.heading}>
          {t("home.summaryHeading")}
        </Text>
        {summary.data ? (
          <Figures summary={summary.data} />
        ) : summary.error ? (
          <LoadError
            message={t("home.summaryFailed")}
            retry={() => void summary.refetch()}
            retrying={summary.isFetching}
          />
        ) : (
          <ActivityIndicator />
        )}
      </View>
      <View style={styles.section}>
        <Text role="heading" style={styles.heading}>
          {t("home.lowStockHeading")}
        </Text>
        {lowStock.data ? (
          <LowStockPreview products={lowStock.data.items} />
        ) : lowStock.error ? (
          <LoadError
            message={t("home.lowStockFailed")}
            retry={() => void lowStock.refetch()}
            retrying={lowStock.isFetching}
          />
        ) : (
          <ActivityIndicator />
        )}
      </View>
    </ScrollView>
  );
}

/** The summary's counts, in a grid of tiles. */
function Figures({ summary }: { summary: Schema<"StatsSummaryResponse"> }) {
  const { t } = useTranslation();
  return (
    <View style={styles.figures}>
      <Figure label={t("home.totalProducts")} value={summary.total_products} />
      <Figure label={t("home.totalUnits")} value={summary.total_units} />
      <Figure label={t("home.lowStockCount")} value={summary.low_stock_count} />
      <Figure
        label={t("home.outOfStockCount")}
        value={summary.out_of_stock_count}
      />
    </View>
  );
}

function Figure({ label, value }: { label: string; value: number }) {
  const { t } = useTranslation();
  return (
    // One element for screen readers, read as the number and then its label.
    <View accessible style={styles.figure}>
      <Text style={styles.figureValue}>{t("home.number", { value })}</Text>
      <Text style={styles.figureLabel}>{label}</Text>
    </View>
  );
}

/** The first few low-stock products, each opening the product, and a way to see them all. */
function LowStockPreview({
  products,
}: {
  products: Schema<"ProductResponse">[];
}) {
  const { t } = useTranslation();
  const router = useRouter();
  if (products.length === 0) {
    return <Text style={styles.text}>{t("home.noLowStock")}</Text>;
  }
  return (
    <>
      <View>
        {products.map((product) => (
          <ProductRow key={product.id} product={product} />
        ))}
      </View>
      <Button
        onPress={() => router.push("/low-stock")}
        title={t("home.seeAllLowStock")}
        variant="secondary"
      />
    </>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 32,
    padding: 24,
  },
  section: {
    gap: 12,
  },
  heading: {
    fontSize: 20,
    fontWeight: "600",
  },
  figures: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
  },
  figure: {
    flexBasis: "45%",
    flexGrow: 1,
    gap: 4,
    padding: 16,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: "#8a8a8e",
  },
  figureValue: {
    fontSize: 28,
    fontWeight: "600",
  },
  figureLabel: {
    fontSize: 14,
  },
  text: {
    fontSize: 16,
  },
});
