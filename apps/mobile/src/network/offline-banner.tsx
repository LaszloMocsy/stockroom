import { SymbolView } from "expo-symbols";
import { use, useEffect, useRef, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { AccessibilityInfo, StyleSheet, Text, View } from "react-native";
import {
  SafeAreaInsetsContext,
  SafeAreaProvider,
} from "react-native-safe-area-context";

import { useOffline } from "./use-offline";

/**
 * Shows a banner across the top of the app while the device is offline, above whichever screen is in
 * front, and the screens below it. The screens get their safe area measured again below the banner, so
 * their headers do not leave room for the status bar that the banner already covers.
 */
export function OfflineBannerLayout({ children }: { children: ReactNode }) {
  return (
    <View style={styles.layout}>
      <OfflineBanner />
      <SafeAreaProvider>{children}</SafeAreaProvider>
    </View>
  );
}

/**
 * Says that the device is offline, and so cannot reach the server, until the network returns. Screen
 * readers announce it when the network drops and when it returns.
 */
export function OfflineBanner() {
  const { t } = useTranslation();
  const offline = useOffline();
  // Expo Router provides the insets; without them, as in some tests, the banner starts at the edge.
  const topInset = use(SafeAreaInsetsContext)?.top ?? 0;

  const announcedOffline = useRef(false);
  useEffect(() => {
    if (offline !== announcedOffline.current) {
      announcedOffline.current = offline;
      AccessibilityInfo.announceForAccessibility(
        offline ? t("offline.message") : t("offline.backOnline"),
      );
    }
  }, [offline, t]);

  if (!offline) {
    return null;
  }
  return (
    <View
      style={[styles.banner, { paddingTop: topInset + 8 }]}
      testID="offline-banner"
    >
      {/* Decorative: the text says the same. */}
      <SymbolView
        name={{ ios: "wifi.slash", android: "wifi_off" }}
        size={20}
        tintColor={styles.message.color}
      />
      <Text style={styles.message}>{t("offline.message")}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  layout: {
    flex: 1,
  },
  banner: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    paddingBottom: 8,
    paddingHorizontal: 16,
    backgroundColor: "#fdecc8",
  },
  message: {
    flex: 1,
    color: "#3d2b00",
    fontSize: 15,
  },
});
