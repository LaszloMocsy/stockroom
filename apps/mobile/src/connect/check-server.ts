import type { InfoResponse } from "@stockroom/api-client";

import { createClient } from "@/api/client";

/**
 * Why the app cannot connect to a server:
 * - `malformed`: what the user typed is not a server address.
 * - `insecure`: the address is `http://`, which this build refuses for servers other than `localhost`.
 * - `unreachable`: no answer, because of the network, a wrong address, or a timeout.
 * - `server_error`: the server, or a proxy in front of it, answered with a 5xx; it may work later.
 * - `not_stockroom`: something answered, but not with a Stockroom server's `/info`.
 * - `app_outdated`, `server_outdated`: a Stockroom server answered, but this app cannot work with it
 *   (see `checkCompatibility`).
 * - `save_failed`: the server is fine, but the app could not store its URL.
 */
export type ConnectFailure =
  | "malformed"
  | "insecure"
  | "unreachable"
  | "server_error"
  | "not_stockroom"
  | "app_outdated"
  | "server_outdated"
  | "save_failed";

export class ConnectError extends Error {
  override readonly name = "ConnectError";

  constructor(
    readonly reason: ConnectFailure,
    /** The server URL that was tried; null when the address was malformed. */
    readonly url: string | null = null,
    /** The HTTP status of a `server_error`. */
    readonly status: number | null = null,
  ) {
    super(
      `Cannot connect to ${url ?? "the server"}: ${reason}${status ? ` (HTTP ${status})` : ""}`,
    );
  }
}

/** How long to wait for `/info` before calling the server unreachable. */
const DefaultTimeoutMs = 10_000;

export interface CheckServerOptions {
  timeoutMs?: number;
  /** The fetch implementation; defaults to `globalThis.fetch`. */
  fetch?: (input: Request) => Promise<Response>;
}

/**
 * Asks the server at `url` (normalised, see `normaliseServerUrl`) for `GET /api/v1/info` and returns
 * the answer if it is a Stockroom server's.
 *
 * @throws {ConnectError} When the server is unreachable, fails, or is not a Stockroom server.
 */
export async function checkServer(
  url: string,
  { timeoutMs = DefaultTimeoutMs, fetch }: CheckServerOptions = {},
): Promise<InfoResponse> {
  const client = createClient({ baseUrl: url, ...(fetch && { fetch }) });
  const abort = new AbortController();
  const timeout = setTimeout(() => abort.abort(), timeoutMs);

  let response: Response;
  let body: unknown;
  try {
    // As text, so that a body that is not JSON, such as a web page, is not mistaken for a network error.
    ({ response, data: body } = await client.GET("/api/v1/info", {
      parseAs: "text",
      signal: abort.signal,
    }));
  } catch {
    throw new ConnectError("unreachable", url);
  } finally {
    clearTimeout(timeout);
  }

  if (response.status >= 500) {
    throw new ConnectError("server_error", url, response.status);
  }
  const info = response.ok ? parseInfo(body) : null;
  if (!info) {
    throw new ConnectError("not_stockroom", url);
  }
  return info;
}

/** Reads an `/info` response body, or returns null when it is not one. */
function parseInfo(body: unknown): InfoResponse | null {
  if (typeof body !== "string") {
    return null;
  }
  let value: unknown;
  try {
    value = JSON.parse(body);
  } catch {
    return null;
  }
  if (
    typeof value === "object" &&
    value !== null &&
    "server_version" in value &&
    "api_version" in value &&
    "min_client_version" in value &&
    "setup_required" in value &&
    typeof value.server_version === "string" &&
    typeof value.api_version === "string" &&
    typeof value.min_client_version === "string" &&
    typeof value.setup_required === "boolean"
  ) {
    return {
      server_version: value.server_version,
      api_version: value.api_version,
      min_client_version: value.min_client_version,
      setup_required: value.setup_required,
    };
  }
  return null;
}
