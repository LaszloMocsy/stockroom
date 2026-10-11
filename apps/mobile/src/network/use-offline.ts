import {
  addNetworkStateListener,
  getNetworkStateAsync,
  type NetworkState,
} from "expo-network";
import { useEffect, useState } from "react";

/**
 * Whether the device has no network connection at all. Only the connection counts, not whether it
 * reaches the internet: a self-hosted server is often on the local network, which a Wi-Fi without
 * internet access (which Android reports as unreachable) still reaches.
 */
export function isOffline(state: NetworkState): boolean {
  return state.isConnected === false;
}

/**
 * Whether the device is offline (see `isOffline`), kept up to date as the network drops and returns.
 * Online until the first reading arrives, so the app does not flash the offline state as it starts.
 */
export function useOffline(): boolean {
  const [offline, setOffline] = useState(false);

  useEffect(() => {
    let current = true;
    let changed = false;
    const subscription = addNetworkStateListener((state) => {
      changed = true;
      setOffline(isOffline(state));
    });
    // A change that arrives first is newer than this reading. Unreadable counts as online.
    getNetworkStateAsync().then(
      (state) => {
        if (current && !changed) {
          setOffline(isOffline(state));
        }
      },
      () => undefined,
    );
    return () => {
      current = false;
      subscription.remove();
    };
  }, []);

  return offline;
}
