import {
  CameraView,
  useCameraPermissions,
  type BarcodeScanningResult,
  type BarcodeType,
} from "expo-camera";
import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  AppState,
  Linking,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { Button } from "@/components/button";

import { createScanFilter } from "./scan-filter";

/** The symbologies products carry (spec 3.1): EAN, UPC, Code 128, and QR. */
export const ScannedBarcodeTypes: BarcodeType[] = [
  "ean13",
  "ean8",
  "upc_a",
  "upc_e",
  "code128",
  "qr",
];

export interface BarcodeScannerProps {
  /** Called with the barcode's payload, once per detection (see `createScanFilter`). */
  onScan: (barcode: string) => void;
  /** Turns the camera off, for example while the screen is not focused or a scan is being handled. */
  paused?: boolean;
}

/**
 * The back camera, scanning barcodes, with the permission flow: it asks for the camera when it has not
 * yet, and, once the user has turned it down for good, explains how to turn it on in Settings.
 */
export function BarcodeScanner({
  onScan,
  paused = false,
}: BarcodeScannerProps) {
  const { t } = useTranslation();
  const [permission, requestPermission, getPermission] = useCameraPermissions();
  const isNew = useRef(createScanFilter()).current;

  // The user may turn the camera on in Settings and come back: check again whenever the app returns.
  const granted = permission?.granted ?? false;
  useEffect(() => {
    if (granted) {
      return;
    }
    const subscription = AppState.addEventListener("change", (status) => {
      if (status === "active") {
        void getPermission();
      }
    });
    return () => subscription.remove();
  }, [granted, getPermission]);

  if (!permission) {
    return (
      <View style={styles.message}>
        <ActivityIndicator />
      </View>
    );
  }

  if (!permission.granted) {
    const canAsk = permission.canAskAgain;
    return (
      <View style={styles.message}>
        <Text style={styles.text}>
          {canAsk
            ? t("scanner.permissionNeeded")
            : t("scanner.permissionDenied")}
        </Text>
        <Button
          onPress={() =>
            void (canAsk ? requestPermission() : Linking.openSettings())
          }
          title={canAsk ? t("scanner.allowCamera") : t("scanner.openSettings")}
        />
      </View>
    );
  }

  const scanned = ({ data }: BarcodeScanningResult) => {
    if (isNew(data, Date.now())) {
      onScan(data);
    }
  };

  return (
    <View style={styles.camera}>
      <CameraView
        active={!paused}
        barcodeScannerSettings={{ barcodeTypes: ScannedBarcodeTypes }}
        facing="back"
        onBarcodeScanned={paused ? undefined : scanned}
        style={StyleSheet.absoluteFill}
        testID="barcode-scanner-camera"
      />
      <Text style={styles.hint}>{t("scanner.hint")}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  camera: {
    flex: 1,
    justifyContent: "flex-end",
    backgroundColor: "#000000",
  },
  hint: {
    margin: 24,
    padding: 12,
    borderRadius: 8,
    overflow: "hidden",
    backgroundColor: "rgba(0, 0, 0, 0.6)",
    color: "#ffffff",
    fontSize: 16,
    textAlign: "center",
  },
  message: {
    flex: 1,
    justifyContent: "center",
    gap: 16,
    padding: 24,
  },
  text: {
    fontSize: 16,
    textAlign: "center",
  },
});
