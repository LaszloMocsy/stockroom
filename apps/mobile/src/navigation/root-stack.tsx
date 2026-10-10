import { Stack } from "expo-router";

import { useServerUrl, useSignedIn } from "@/api/provider";
import { useServerInfo } from "@/api/server-info";
import { compatibilityWith } from "@/compatibility/compatibility";

/**
 * The app's screens, each available only in the state it belongs to: the Connect screen until a server
 * is stored, then the "update needed" screen while the app and the server cannot work together, the
 * setup screen while the server has no users, the login screen until the user signs in, and then the
 * tabs.
 *
 * Until the server's `/info` arrives, or when it fails, the app behaves as if the server were compatible
 * and set up, so an app that cannot reach its server stays usable and its screens show their own errors.
 */
export function RootStack() {
  const hasServer = useServerUrl() !== null;
  const { data: info } = useServerInfo();
  const compatibility = info ? compatibilityWith(info) : null;
  const compatible = compatibility === null || compatibility === "ok";
  const setupRequired = info?.setup_required === true;
  const ready = hasServer && compatible && !setupRequired;
  const signedIn = useSignedIn();

  return (
    <Stack>
      <Stack.Protected guard={ready && signedIn}>
        <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
      </Stack.Protected>
      <Stack.Protected guard={ready && !signedIn}>
        <Stack.Screen name="login" />
      </Stack.Protected>
      <Stack.Protected guard={hasServer && compatible && setupRequired}>
        <Stack.Screen name="setup" />
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
