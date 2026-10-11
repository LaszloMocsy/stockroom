import { useLocalSearchParams } from "expo-router";

import { StockSheet } from "@/stock/stock-sheet";

export default function AddStockScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <StockSheet productId={id} type="receive" />;
}
