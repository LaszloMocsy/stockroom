import { useLocalSearchParams } from "expo-router";

import { CreateProductForm } from "@/products/create-product-form";

export default function NewProductScreen() {
  const { barcode } = useLocalSearchParams<{ barcode?: string }>();
  return <CreateProductForm barcode={barcode} />;
}
