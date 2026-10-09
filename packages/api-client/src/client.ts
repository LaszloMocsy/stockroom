import createClient, { type Client, type Middleware } from "openapi-fetch";
import type { paths } from "./generated/schema.js";

/** A fetch client whose paths, parameters, request bodies, and responses are typed from the API contract. */
export type ApiClient = Client<paths>;

/** Headers every request carries, so the server knows which app and version is calling (spec 10.1). */
export const ClientHeaders = {
  version: "X-Client-Version",
  platform: "X-Client-Platform",
} as const;

export interface ApiClientOptions {
  /**
   * The server URL, for example `https://stock.example.com`, without `/api/v1`: request paths include it.
   * An empty string sends requests to the current origin (a web app served behind the API's proxy).
   */
  baseUrl: string;
  /** The app's version, sent as `X-Client-Version`. */
  clientVersion: string;
  /** The app's platform, sent as `X-Client-Platform`, for example `ios`, `android`, or `web`. */
  clientPlatform: string;
  /**
   * Returns the current access token, or nothing when signed out. Called for every request, so a new
   * token is used as soon as the app stores it.
   */
  getAccessToken?: () =>
    string | null | undefined | Promise<string | null | undefined>;
  /** The fetch implementation; defaults to `globalThis.fetch`. */
  fetch?: (input: Request) => Promise<Response>;
}

/**
 * Creates a typed client for the Stockroom API. Paths are the full API paths, for example
 * `client.GET("/api/v1/products/{id}", { params: { path: { id } } })`.
 */
export function createApiClient(options: ApiClientOptions): ApiClient {
  const client = createClient<paths>({
    baseUrl: normaliseBaseUrl(options.baseUrl),
    ...(options.fetch && { fetch: options.fetch }),
  });
  client.use(clientHeaders(options));
  return client;
}

/** Trims whitespace and trailing slashes, so `https://host/` and `https://host` build the same URLs. */
export function normaliseBaseUrl(baseUrl: string): string {
  return baseUrl.trim().replace(/\/+$/, "");
}

function clientHeaders(options: ApiClientOptions): Middleware {
  return {
    async onRequest({ request }) {
      request.headers.set(ClientHeaders.version, options.clientVersion);
      request.headers.set(ClientHeaders.platform, options.clientPlatform);

      // A caller that sets Authorization itself (for example a retry with a new token) wins.
      if (!request.headers.has("Authorization")) {
        const token = await options.getAccessToken?.();
        if (token) {
          request.headers.set("Authorization", `Bearer ${token}`);
        }
      }

      return request;
    },
  };
}
