import { Stack } from "expo-router";
import { StatusBar } from "expo-status-bar";

import { defaultServerUrl } from "@/api/client";
import { ApiProvider } from "@/api/provider";
import { useAppStateFocus } from "@/api/query-client";
import { secureStore } from "@/storage/secure-store";
import { createAppStorage } from "@/storage/storage";

const storage = createAppStorage(secureStore);

export default function RootLayout() {
  useAppStateFocus();

  return (
    <ApiProvider storage={storage} defaultServerUrl={defaultServerUrl}>
      <Stack />
      <StatusBar style="auto" />
    </ApiProvider>
  );
}
