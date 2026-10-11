import { SymbolView } from "expo-symbols";
import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

import type { StockStatus } from "./stock-status";

/**
 * Marks a product that is low on or out of stock with an icon and words as well as colour (spec 6), so
 * the colour is never the only signal. Renders nothing for stock that is fine.
 */
export function StockBadge({ status }: { status: StockStatus }) {
  const { t } = useTranslation();
  if (status === "ok") {
    return null;
  }
  const out = status === "out";
  const color = out ? "#8c1d18" : "#6b3a00";
  return (
    <View style={[styles.badge, out ? styles.out : styles.low]}>
      {/* Decorative: the text says the same. */}
      <SymbolView
        name={{ ios: "exclamationmark.triangle.fill", android: "warning" }}
        size={18}
        tintColor={color}
      />
      <Text style={[styles.text, { color }]}>
        {out ? t("stock.outOfStock") : t("stock.lowStock")}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: {
    flexDirection: "row",
    alignItems: "center",
    alignSelf: "flex-start",
    gap: 6,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 999,
    borderWidth: 1,
  },
  low: {
    backgroundColor: "#fff4e0",
    borderColor: "#6b3a00",
  },
  out: {
    backgroundColor: "#fdeceb",
    borderColor: "#8c1d18",
  },
  text: {
    fontSize: 14,
    fontWeight: "600",
  },
});
