import { Stack } from "expo-router";

import { useServerUrl } from "@/api/provider";

/** The app's screens, each available only in the state it belongs to: the Connect screen until a server is stored. */
export function RootStack() {
  const hasServer = useServerUrl() !== null;

  return (
    <Stack>
      <Stack.Protected guard={hasServer}>
        <Stack.Screen name="index" />
      </Stack.Protected>
      <Stack.Protected guard={!hasServer}>
        <Stack.Screen name="connect" />
      </Stack.Protected>
    </Stack>
  );
}
