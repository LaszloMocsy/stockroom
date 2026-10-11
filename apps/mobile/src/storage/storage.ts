/**
 * A persistent store of strings by key. On devices this is `secureStore` (the Keychain on iOS, encrypted
 * with the Android Keystore on Android); tests pass an in-memory fake. Keys may contain only letters,
 * digits, `.`, `-`, and `_`.
 */
export interface KeyValueStore {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
  deleteItem(key: string): Promise<void>;
}

/** The tokens from login or refresh that the app keeps between launches. */
export interface StoredTokens {
  accessToken: string;
  refreshToken: string;
}

/** What the app keeps between launches: the server it connects to and the session's tokens (spec 9.2, 10.2). */
export interface AppStorage {
  /** The server URL saved when connecting, or null before the first connect. */
  getServerUrl(): Promise<string | null>;
  setServerUrl(url: string): Promise<void>;
  /** The stored tokens, or null when signed out. */
  getTokens(): Promise<StoredTokens | null>;
  /** Replaces both tokens at once; a refresh rotates both. */
  setTokens(tokens: StoredTokens): Promise<void>;
  /** Signs out: forgets the tokens but keeps the server. */
  clearTokens(): Promise<void>;
  /** Forgets the server and the tokens, which belong to it. */
  clear(): Promise<void>;
}

export const StorageKeys = {
  serverUrl: "stockroom.server_url",
  tokens: "stockroom.tokens",
} as const;

export function createAppStorage(store: KeyValueStore): AppStorage {
  return {
    getServerUrl: () => store.getItem(StorageKeys.serverUrl),

    setServerUrl: (url) => store.setItem(StorageKeys.serverUrl, url),

    async getTokens() {
      const stored = await store.getItem(StorageKeys.tokens);
      return stored === null ? null : parseTokens(stored);
    },

    // One entry for both, so an app killed mid-write cannot keep a new access token with a used-up
    // refresh token.
    setTokens: ({ accessToken, refreshToken }) =>
      store.setItem(
        StorageKeys.tokens,
        JSON.stringify({ accessToken, refreshToken }),
      ),

    clearTokens: () => store.deleteItem(StorageKeys.tokens),

    async clear() {
      // Tokens first: if this fails halfway, the app is signed out rather than signed in to no server.
      await store.deleteItem(StorageKeys.tokens);
      await store.deleteItem(StorageKeys.serverUrl);
    },
  };
}

/** Reads stored tokens; anything unreadable counts as signed out. */
function parseTokens(stored: string): StoredTokens | null {
  try {
    const value: unknown = JSON.parse(stored);
    if (
      typeof value === "object" &&
      value !== null &&
      "accessToken" in value &&
      "refreshToken" in value &&
      typeof value.accessToken === "string" &&
      typeof value.refreshToken === "string"
    ) {
      return {
        accessToken: value.accessToken,
        refreshToken: value.refreshToken,
      };
    }
  } catch {
    // Not JSON: fall through.
  }
  return null;
}
