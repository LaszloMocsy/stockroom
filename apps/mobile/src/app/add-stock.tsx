import { useLocalSearchParams } from "expo-router";

import { AddStock } from "@/stock/add-stock";

export default function AddStockScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <AddStock productId={id} />;
}
