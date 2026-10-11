import { useLocalSearchParams } from "expo-router";

import { StockSheet } from "@/stock/stock-sheet";

export default function SetCountScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <StockSheet productId={id} type="adjust" />;
}
