import { Stack } from "expo-router";

import { useServerUrl } from "@/api/provider";
import { useCompatibility } from "@/compatibility/compatibility";

/**
 * The app's screens, each available only in the state it belongs to: the Connect screen until a server
 * is stored, and then the "update needed" screen while the app and the server cannot work together.
 */
export function RootStack() {
  const hasServer = useServerUrl() !== null;
  const compatibility = useCompatibility();
  // Unknown counts as compatible; see useCompatibility.
  const compatible = compatibility === null || compatibility === "ok";

  return (
    <Stack>
      <Stack.Protected guard={hasServer && compatible}>
        <Stack.Screen name="index" />
      </Stack.Protected>
      <Stack.Protected guard={hasServer && !compatible}>
        <Stack.Screen name="update-required" />
      </Stack.Protected>
      <Stack.Protected guard={!hasServer}>
        <Stack.Screen name="connect" />
      </Stack.Protected>
    </Stack>
  );
}
