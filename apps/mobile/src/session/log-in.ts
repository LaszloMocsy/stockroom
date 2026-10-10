import { unwrap } from "@stockroom/api-client";
import Constants from "expo-constants";
import { useCallback } from "react";

import { useApiClient, useSaveTokens } from "@/api/provider";

export interface Credentials {
  username: string;
  password: string;
}

/** The server keeps up to this many characters of a session's device name. */
const MaxDeviceNameLength = 100;

/** Names the session after this device, for example "Anna's iPhone", so an ADMIN can tell sessions apart (spec 10.2). */
const deviceName = Constants.deviceName?.slice(0, MaxDeviceNameLength) || null;

/**
 * Logs in to the current server and stores the session's tokens.
 *
 * @throws {ApiResponseError} When the server refuses, for example `invalid_credentials` (401).
 */
export function useLogIn(): (credentials: Credentials) => Promise<void> {
  const client = useApiClient();
  const saveTokens = useSaveTokens();

  return useCallback(
    async ({ username, password }) => {
      const tokens = await unwrap(
        client.POST("/api/v1/auth/login", {
          body: { username, password, device_name: deviceName },
        }),
      );
      await saveTokens({
        accessToken: tokens.access_token,
        refreshToken: tokens.refresh_token,
      });
    },
    [client, saveTokens],
  );
}
