import { ApiResponseError, unwrap } from "@stockroom/api-client";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useIsFocused, useNavigation, useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";

import { useApiClient } from "@/api/provider";
import { LoadError } from "@/components/load-error";
import { productKey } from "@/products/use-products";

import { BarcodeScanner } from "./barcode-scanner";

/**
 * The Scan tab (spec 4.2): a scanned barcode is looked up and its product opens, archived ones
 * included. A barcode that no product has opens a sheet that offers to create a product with it or to
 * attach it to an existing one (spec 5, flow 4). The camera is off while another tab or screen is in front.
 */
export function Scan() {
  const { t } = useTranslation();
  const client = useApiClient();
  const queryClient = useQueryClient();
  const router = useRouter();
  const navigation = useNavigation();
  const focused = useIsFocused();
  const lookUp = useMutation({
    mutationFn: (barcode: string) =>
      unwrap(
        client.GET("/api/v1/products/lookup", {
          params: { query: { barcode } },
        }),
      ),
    // The product screen shows it at once, while it loads the latest.
    onSuccess: (product) =>
      queryClient.setQueryData(productKey(product.id), product),
  });

  // Neither opens anything when the user moved on to another tab while the barcode was looked up.
  const open = (barcode: string) =>
    lookUp.mutate(barcode, {
      onSuccess: (product) => {
        lookUp.reset();
        if (navigation.isFocused()) {
          router.push({
            pathname: "/product/[id]",
            params: { id: product.id },
          });
        }
      },
      onError: (error) => {
        if (error instanceof ApiResponseError && error.status === 404) {
          lookUp.reset();
          if (navigation.isFocused()) {
            router.push({ pathname: "/unknown-barcode", params: { barcode } });
          }
        }
      },
    });

  const scanned = (barcode: string) => {
    if (!lookUp.isPending) {
      open(barcode);
    }
  };

  const barcode = lookUp.variables ?? "";
  let status = null;
  if (lookUp.isPending) {
    status = (
      <View style={styles.pending}>
        <ActivityIndicator />
        <Text role="status" style={styles.text}>
          {t("scan.lookingUp", { barcode })}
        </Text>
      </View>
    );
  } else if (lookUp.error) {
    status = (
      <LoadError
        message={t("scan.lookUpFailed", { barcode })}
        retry={() => open(barcode)}
        retrying={false}
      />
    );
  }

  return (
    <View style={styles.container}>
      <BarcodeScanner onScan={scanned} paused={!focused} />
      {status && <View style={styles.status}>{status}</View>}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  status: {
    padding: 16,
  },
  pending: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
  },
  text: {
    fontSize: 16,
    textAlign: "center",
  },
});
