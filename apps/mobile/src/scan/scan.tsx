import { useIsFocused } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

import { BarcodeScanner } from "./barcode-scanner";

/** The Scan tab: the camera, and the last barcode it read. The camera is off while another tab is open. */
export function Scan() {
  const { t } = useTranslation();
  const focused = useIsFocused();
  const [barcode, setBarcode] = useState<string | null>(null);

  return (
    <View style={styles.container}>
      <BarcodeScanner onScan={setBarcode} paused={!focused} />
      {barcode !== null && (
        <Text role="status" style={styles.result}>
          {t("scan.scanned", { barcode })}
        </Text>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  result: {
    padding: 16,
    fontSize: 16,
    textAlign: "center",
  },
});
