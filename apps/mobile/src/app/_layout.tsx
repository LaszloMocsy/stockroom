import "@/i18n";

import { StatusBar } from "expo-status-bar";

import { useAppStateFocus } from "@/api/query-client";
import { AppProviders } from "@/navigation/app-providers";
import { RootStack } from "@/navigation/root-stack";
import { secureStore } from "@/storage/secure-store";
import { createAppStorage } from "@/storage/storage";

const storage = createAppStorage(secureStore);

export default function RootLayout() {
  useAppStateFocus();

  return (
    <AppProviders storage={storage}>
      <RootStack />
      <StatusBar style="auto" />
    </AppProviders>
  );
}
