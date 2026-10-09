import createClient, {
  type Client,
  type ClientOptions,
  type Middleware,
} from "openapi-fetch";
import type { components, paths } from "./generated/schema.js";

/** A fetch client whose paths, parameters, request bodies, and responses are typed from the API contract. */
export type ApiClient = Client<paths>;

/** The access and refresh tokens returned by login and refresh. */
export type TokenResponse = components["schemas"]["TokenResponse"];

type MaybePromise<T> = T | Promise<T>;

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
  getAccessToken?: () => MaybePromise<string | null | undefined>;
  /** Refreshes an expired access token automatically. Without it, a 401 is returned to the caller as is. */
  tokenRefresh?: TokenRefreshOptions;
  /** The fetch implementation; defaults to `globalThis.fetch`. */
  fetch?: (input: Request) => Promise<Response>;
}

/**
 * When a request that carried an access token gets a 401, the client exchanges the refresh token for new
 * tokens and retries the request once. Requests that fail together share one refresh: refresh tokens
 * rotate, and the server treats a second use of the same one as theft and ends the session (spec 10.2).
 */
export interface TokenRefreshOptions {
  /** Returns the stored refresh token, or nothing when signed out. */
  getRefreshToken: () => MaybePromise<string | null | undefined>;
  /**
   * Stores the tokens from a refresh. Both are new and the old refresh token is used up, so store both,
   * and make `getAccessToken` return the new access token by the time this settles.
   */
  onTokensRefreshed: (tokens: TokenResponse) => MaybePromise<void>;
  /**
   * The server rejected the refresh token, or none is stored: the session is over and the user has to log
   * in again. Not called for rate limiting or server errors, which leave the tokens for a later attempt;
   * the caller gets the original 401 either way.
   */
  onSessionExpired?: () => MaybePromise<void>;
}

/** Login, refresh, and logout answer 401 for bad credentials or tokens; refreshing would not help. */
const AuthPathPrefix = "/api/v1/auth/";

/**
 * Creates a typed client for the Stockroom API. Paths are the full API paths, for example
 * `client.GET("/api/v1/products/{id}", { params: { path: { id } } })`.
 */
export function createApiClient(options: ApiClientOptions): ApiClient {
  const clientOptions: ClientOptions = {
    baseUrl: normaliseBaseUrl(options.baseUrl),
    ...(options.fetch && { fetch: options.fetch }),
  };
  const client = createClient<paths>(clientOptions);
  client.use(authentication(options, clientOptions));
  return client;
}

/** Trims whitespace and trailing slashes, so `https://host/` and `https://host` build the same URLs. */
export function normaliseBaseUrl(baseUrl: string): string {
  return baseUrl.trim().replace(/\/+$/, "");
}

function setClientHeaders(request: Request, options: ApiClientOptions) {
  request.headers.set(ClientHeaders.version, options.clientVersion);
  request.headers.set(ClientHeaders.platform, options.clientPlatform);
}

function bearer(token: string) {
  return `Bearer ${token}`;
}

/** Sets the client headers and the access token, and refreshes and retries on a 401. */
function authentication(
  options: ApiClientOptions,
  clientOptions: ClientOptions,
): Middleware {
  const { tokenRefresh } = options;
  const fetch =
    options.fetch ?? ((request: Request) => globalThis.fetch(request));

  /** Copies of requests that may need a retry, by request id, with the access token they were sent with. */
  const retryable = new Map<string, { request: Request; token: string }>();
  let refreshing: Promise<string | null> | null = null;

  // The refresh request goes through a separate client, so it carries no expired access token and a
  // 401 from it cannot start another refresh.
  const refreshClient = createClient<paths>(clientOptions);
  refreshClient.use({
    onRequest: ({ request }) => {
      setClientHeaders(request, options);
      return request;
    },
  });

  /** Exchanges the refresh token for new tokens; resolves to the new access token, or null on failure. */
  async function refreshTokens(
    settings: TokenRefreshOptions,
  ): Promise<string | null> {
    const refreshToken = await settings.getRefreshToken();
    if (!refreshToken) {
      await settings.onSessionExpired?.();
      return null;
    }

    const { data, response } = await refreshClient.POST(
      "/api/v1/auth/refresh",
      { body: { refresh_token: refreshToken } },
    );
    if (data) {
      await settings.onTokensRefreshed(data);
      return data.access_token;
    }

    if (response.status === 401) {
      await settings.onSessionExpired?.();
    }
    return null;
  }

  /** A usable access token in place of the rejected one, refreshing at most once at a time. */
  async function replacementToken(
    settings: TokenRefreshOptions,
    rejected: string,
  ): Promise<string | null> {
    if (!refreshing) {
      // Another request may have refreshed since this one was sent.
      const current = await options.getAccessToken?.();
      if (current && current !== rejected) {
        return current;
      }

      // Checked again: another request may have started a refresh during the await.
      refreshing ??= refreshTokens(settings).finally(() => {
        refreshing = null;
      });
    }
    return refreshing;
  }

  return {
    async onRequest({ id, schemaPath, request }) {
      setClientHeaders(request, options);

      // A caller that sets Authorization itself wins, and its request is not retried.
      if (request.headers.has("Authorization")) {
        return request;
      }

      const token = await options.getAccessToken?.();
      if (!token) {
        return request;
      }

      request.headers.set("Authorization", bearer(token));
      if (tokenRefresh && !schemaPath.startsWith(AuthPathPrefix)) {
        // fetch consumes the body, so keep a copy to send again.
        retryable.set(id, { request: request.clone(), token });
      }
      return request;
    },

    async onResponse({ id, response }) {
      const sent = retryable.get(id);
      retryable.delete(id);
      if (!sent || !tokenRefresh || response.status !== 401) {
        return undefined;
      }

      const token = await replacementToken(tokenRefresh, sent.token);
      if (!token) {
        return undefined;
      }

      // Retried once, outside the middleware: a second 401 goes back to the caller.
      sent.request.headers.set("Authorization", bearer(token));
      return fetch(sent.request);
    },

    onError({ id }) {
      retryable.delete(id);
    },
  };
}
