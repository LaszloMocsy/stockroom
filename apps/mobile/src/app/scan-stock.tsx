import { Stack, useLocalSearchParams } from "expo-router";
import { useTranslation } from "react-i18next";

import { Scan, type ScanMode } from "@/scan/scan";

/** The scanner that opens the sheet adding stock to the scanned product (`type=receive`) or removing it. */
export default function ScanStockScreen() {
  const { t } = useTranslation();
  const { type } = useLocalSearchParams<{ type?: string }>();
  const mode: ScanMode = type === "issue" ? "issue" : "receive";
  return (
    <>
      <Stack.Screen options={{ title: t(`scan.${mode}Title`) }} />
      <Scan mode={mode} />
    </>
  );
}
