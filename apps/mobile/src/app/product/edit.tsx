import { useLocalSearchParams } from "expo-router";

import { EditProduct } from "@/products/edit-product-form";

export default function EditProductScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <EditProduct id={id} />;
}
