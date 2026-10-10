import { useLocalSearchParams } from "expo-router";

import { UnknownBarcode } from "@/scan/unknown-barcode";

export default function UnknownBarcodeScreen() {
  const { barcode } = useLocalSearchParams<{ barcode?: string }>();
  return <UnknownBarcode barcode={barcode ?? ""} />;
}
