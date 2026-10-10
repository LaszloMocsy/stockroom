import {
  createApiClient,
  type ApiClient,
  type ApiClientOptions,
} from "@stockroom/api-client";
import Constants from "expo-constants";
import { Platform } from "react-native";

/**
 * The server the app talks to during development, until the user can choose one. Set
 * `EXPO_PUBLIC_API_URL` to reach the API elsewhere, for example `http://10.0.2.2:5278` from the
 * Android emulator or the computer's network address from a phone.
 */
export const devServerUrl =
  process.env.EXPO_PUBLIC_API_URL ?? "http://localhost:5278";

/** The app's version from `app.json`, sent as `X-Client-Version`. */
export const clientVersion = Constants.expoConfig?.version ?? "0.0.0";

/** Creates an API client that identifies this app and platform to the server. */
export function createClient(
  options: Omit<ApiClientOptions, "clientVersion" | "clientPlatform">,
): ApiClient {
  return createApiClient({
    ...options,
    clientVersion,
    clientPlatform: Platform.OS,
  });
}
