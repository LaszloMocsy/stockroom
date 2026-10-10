import { useLocalSearchParams } from "expo-router";

import { ProductDetail } from "@/products/product-detail";

export default function ProductScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <ProductDetail id={id} />;
}
