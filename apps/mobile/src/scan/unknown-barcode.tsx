import { useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

import { Button } from "@/components/button";

/**
 * The sheet for a scanned barcode that no product has (spec 5, flow 4): it offers to create a product
 * with the barcode, or to attach the barcode to an existing product. Either replaces the sheet, so going
 * back from it returns to the scanner.
 */
export function UnknownBarcode({ barcode }: { barcode: string }) {
  const { t } = useTranslation();
  const router = useRouter();

  return (
    <View style={styles.container}>
      <Text role="heading" style={styles.title}>
        {t("unknownBarcode.title")}
      </Text>
      <Text style={styles.text}>
        {t("unknownBarcode.message", { barcode })}
      </Text>
      <Button
        onPress={() =>
          router.replace({ pathname: "/product/new", params: { barcode } })
        }
        title={t("unknownBarcode.createProduct")}
      />
      <Button
        onPress={() =>
          router.replace({ pathname: "/attach-barcode", params: { barcode } })
        }
        title={t("unknownBarcode.attachToExisting")}
        variant="secondary"
      />
      <Button
        onPress={() => router.back()}
        title={t("common.cancel")}
        variant="secondary"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 12,
    padding: 24,
  },
  title: {
    fontSize: 20,
    fontWeight: "600",
  },
  text: {
    fontSize: 16,
  },
});
