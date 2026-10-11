import { ApiResponseError, unwrap, type Schema } from "@stockroom/api-client";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import { useRouter } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ActivityIndicator, Alert, StyleSheet, Text, View } from "react-native";

import { useApiClient } from "@/api/provider";
import { Button } from "@/components/button";
import { TextField } from "@/components/text-field";
import { useDebouncedValue } from "@/hooks/use-debounced-value";

import { SearchDelayMs } from "./product-list";
import { ProductPages } from "./product-pages";
import { productKey, useProducts } from "./use-products";

type Product = Schema<"ProductResponse">;

/** The API's error code for a barcode that another product already has. */
const BarcodeTaken = "barcode_taken";

/**
 * Attaches `barcode` to an existing product (spec 4.1, and spec 5, flow 4): the user searches for the
 * product, picks it, and confirms. The screen then replaces itself with the product, and scanning the
 * barcode opens that product from then on.
 */
export function AttachBarcode({ barcode }: { barcode: string }) {
  const { t } = useTranslation();
  const client = useApiClient();
  const queryClient = useQueryClient();
  const router = useRouter();
  const [search, setSearch] = useState("");
  const query = useDebouncedValue(search, SearchDelayMs).trim();
  const products = useProducts({ search: query });

  const attach = useMutation({
    mutationFn: (product: Product) =>
      unwrap(
        client.POST("/api/v1/products/{id}/barcodes", {
          params: { path: { id: product.id } },
          body: { barcode },
        }),
      ),
    onSuccess: async (product) => {
      // Searches by barcode may find it now.
      await queryClient.invalidateQueries({ queryKey: ["products"] });
      queryClient.setQueryData(productKey(product.id), product);
      router.replace({ pathname: "/product/[id]", params: { id: product.id } });
    },
    onError: (error) => {
      // The product was deleted meanwhile: the list should no longer offer it.
      if (error instanceof ApiResponseError && error.status === 404) {
        void queryClient.invalidateQueries({ queryKey: ["products"] });
      }
    },
  });
  // Stays busy after success too, until the product's screen replaces this one.
  const busy = attach.isPending || attach.isSuccess;

  if (!barcode) {
    return (
      <View style={styles.header}>
        <Text role="alert" style={styles.text}>
          {t("attachBarcode.noBarcode")}
        </Text>
      </View>
    );
  }

  const confirm = (product: Product) => {
    if (busy) {
      return;
    }
    Alert.alert(
      t("attachBarcode.confirmTitle"),
      t("attachBarcode.confirmMessage", {
        barcode,
        name: product.name,
        sku: product.sku,
      }),
      [
        { text: t("common.cancel"), style: "cancel" },
        {
          text: t("attachBarcode.attach"),
          onPress: () => attach.mutate(product),
        },
      ],
    );
  };

  const owner = takenBy(attach.error);
  let status = null;
  if (busy) {
    status = (
      <View style={styles.pending}>
        <ActivityIndicator />
        <Text role="status" style={styles.text}>
          {t("attachBarcode.attaching", { barcode })}
        </Text>
      </View>
    );
  } else if (attach.error) {
    status = (
      <>
        <Text role="alert" style={styles.error}>
          {errorText(attach.error, barcode, attach.variables?.name ?? "", t)}
        </Text>
        {owner && (
          <Button
            onPress={() =>
              router.replace({
                pathname: "/product/[id]",
                params: { id: owner },
              })
            }
            title={t("attachBarcode.openOwner")}
            variant="secondary"
          />
        )}
      </>
    );
  }

  return (
    <View style={styles.screen}>
      <View style={styles.header}>
        <Text style={styles.text}>{t("attachBarcode.intro", { barcode })}</Text>
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
        {status}
      </View>
      <ProductPages
        emptyMessage={
          query
            ? t("products.noMatches", { search: query })
            : t("products.none")
        }
        onPick={confirm}
        products={products}
        testID="attach-barcode-products"
      />
    </View>
  );
}

/** The ID of the product that already has the barcode, from a `barcode_taken` error. */
function takenBy(error: Error | null): string | null {
  if (!(error instanceof ApiResponseError) || error.code !== BarcodeTaken) {
    return null;
  }
  const { details } = error;
  return typeof details === "object" &&
    details !== null &&
    "product_id" in details &&
    typeof details.product_id === "string"
    ? details.product_id
    : null;
}

function errorText(
  error: Error,
  barcode: string,
  name: string,
  t: TFunction,
): string {
  if (!(error instanceof ApiResponseError)) {
    return t("attachBarcode.errors.unreachable");
  }
  if (error.code === BarcodeTaken) {
    return t("attachBarcode.errors.barcodeTaken", { barcode });
  }
  if (error.status === 404) {
    return t("attachBarcode.errors.notFound", { name });
  }
  return t("attachBarcode.errors.serverError", {
    status: String(error.status),
  });
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
  },
  header: {
    gap: 12,
    paddingHorizontal: 24,
    paddingTop: 16,
    paddingBottom: 8,
  },
  pending: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  text: {
    fontSize: 16,
  },
  error: {
    color: "#b3261e",
    fontSize: 16,
  },
});
