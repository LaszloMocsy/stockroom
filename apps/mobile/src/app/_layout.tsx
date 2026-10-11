import "@/i18n";

import { StatusBar } from "expo-status-bar";

import { useAppStateFocus } from "@/api/query-client";
import { AppProviders } from "@/navigation/app-providers";
import { RootStack } from "@/navigation/root-stack";
import { OfflineBannerLayout } from "@/network/offline-banner";
import { secureStore } from "@/storage/secure-store";
import { createAppStorage } from "@/storage/storage";

const storage = createAppStorage(secureStore);

export default function RootLayout() {
  useAppStateFocus();

  return (
    <AppProviders storage={storage}>
      <OfflineBannerLayout>
        <RootStack />
      </OfflineBannerLayout>
      <StatusBar style="auto" />
    </AppProviders>
  );
}
