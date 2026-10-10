import {
  checkCompatibility,
  type Compatibility,
  type InfoResponse,
} from "@stockroom/api-client";

import { clientVersion } from "@/api/client";

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
