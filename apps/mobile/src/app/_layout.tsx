import "@/i18n";

import { StatusBar } from "expo-status-bar";

import { ApiProvider } from "@/api/provider";
import { useAppStateFocus } from "@/api/query-client";
import { RootStack } from "@/navigation/root-stack";
import { secureStore } from "@/storage/secure-store";
import { createAppStorage } from "@/storage/storage";

const storage = createAppStorage(secureStore);

export default function RootLayout() {
  useAppStateFocus();

  return (
    <ApiProvider storage={storage}>
      <RootStack />
      <StatusBar style="auto" />
    </ApiProvider>
  );
}
