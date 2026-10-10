import {
  checkCompatibility,
  type Compatibility,
  type InfoResponse,
} from "@stockroom/api-client";

import { clientVersion } from "@/api/client";
import { useServerInfo } from "@/api/server-info";

/**
 * Whether this app can work with the server whose `/info` this is (spec 10.1), or null when its versions
 * are not semantic versions, so it cannot be a Stockroom server.
 */
export function compatibilityWith(
  info: Pick<InfoResponse, "api_version" | "min_client_version">,
): Compatibility | null {
  try {
    return checkCompatibility(info, clientVersion);
  } catch {
    return null;
  }
}

/**
 * Whether this app can work with the current server, or null while that is unknown: no server is set,
 * its `/info` is loading or failed, or the versions are unreadable. Unknown counts as compatible, so an
 * app that cannot reach its server stays usable and its screens show their own errors.
 */
export function useCompatibility(): Compatibility | null {
  const { data } = useServerInfo();
  return data ? compatibilityWith(data) : null;
}
