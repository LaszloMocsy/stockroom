import { useLocalSearchParams } from "expo-router";

import { AttachBarcode } from "@/products/attach-barcode";

export default function AttachBarcodeScreen() {
  const { barcode } = useLocalSearchParams<{ barcode?: string }>();
  return <AttachBarcode barcode={barcode ?? ""} />;
}
