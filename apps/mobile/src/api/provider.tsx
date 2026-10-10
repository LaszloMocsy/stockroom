import type { ApiClient } from "@stockroom/api-client";
import { QueryClientProvider } from "@tanstack/react-query";
import {
  createContext,
  use,
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";

import type { AppStorage, StoredTokens } from "@/storage/storage";

import { createClient } from "./client";
import { createQueryClient } from "./query-client";

interface ApiContextValue {
  serverUrl: string | null;
  client: ApiClient | null;
  saveServerUrl: (url: string) => Promise<void>;
  saveTokens: (tokens: StoredTokens) => Promise<void>;
  signedIn: boolean;
}

const ApiContext = createContext<ApiContextValue | null>(null);

export interface ApiProviderProps {
  storage: AppStorage;
  children: ReactNode;
}

/**
 * Provides an API client for the stored server and a TanStack Query cache for it. Renders nothing until
 * it has read the stored server URL. The client sends the access token saved with `useSaveTokens`, which
 * also signs the user in.
 */
export function ApiProvider({ storage, children }: ApiProviderProps) {
  // Undefined while the stored URL is being read.
  const [serverUrl, setServerUrl] = useState<string | null>();
  const [session] = useState(createSessionTokens);
  const [signedIn, setSignedIn] = useState(false);

  useEffect(() => {
    let current = true;
    // Cannot reject: an unreadable URL counts as none stored.
    void storage
      .getServerUrl()
      .catch(() => null)
      .then((stored) => {
        if (current) {
          setServerUrl(stored);
        }
      });
    return () => {
      current = false;
    };
  }, [storage]);

  const saveServerUrl = useCallback(
    async (url: string) => {
      await storage.setServerUrl(url);
      setServerUrl(url);
    },
    [storage],
  );

  const saveTokens = useCallback(
    async (newTokens: StoredTokens) => {
      await storage.setTokens(newTokens);
      session.set(newTokens);
      setSignedIn(true);
    },
    [storage, session],
  );

  // A client and a new cache for each server, so one server's data never shows for another.
  const api = useMemo(
    () =>
      serverUrl === undefined
        ? null
        : {
            client:
              serverUrl === null
                ? null
                : createClient({
                    baseUrl: serverUrl,
                    getAccessToken: () => session.get()?.accessToken,
                  }),
            queryClient: createQueryClient(),
          },
    [serverUrl, session],
  );

  const context = useMemo(
    () =>
      serverUrl === undefined || !api
        ? null
        : {
            serverUrl,
            client: api.client,
            saveServerUrl,
            saveTokens,
            signedIn,
          },
    [serverUrl, api, saveServerUrl, saveTokens, signedIn],
  );

  // Drops a cache as soon as it is replaced or unmounted, rather than leaving its queries and mutations
  // to garbage collection, whose five-minute timers would also keep a test run alive. React cleans up
  // this effect before the hooks below unsubscribe, which schedules that collection again, so clear
  // afterwards. Clearing stops the queries' timers but not the mutations', so stop those first.
  const queryClient = api?.queryClient;
  useEffect(
    () => () => {
      if (queryClient) {
        queueMicrotask(() => {
          for (const mutation of queryClient.getMutationCache().getAll()) {
            mutation.destroy();
          }
          queryClient.clear();
        });
      }
    },
    [queryClient],
  );

  if (!api || !context) {
    return null;
  }
  return (
    <ApiContext value={context}>
      <QueryClientProvider client={api.queryClient}>
        {children}
      </QueryClientProvider>
    </ApiContext>
  );
}

/**
 * The session's tokens, outside React state: the client reads them for every request, so a new token is
 * used at once without a re-render. Restoring the stored tokens on launch and refreshing them come with
 * session handling.
 */
function createSessionTokens() {
  let tokens: StoredTokens | null = null;
  return {
    get: () => tokens,
    set: (newTokens: StoredTokens | null) => {
      tokens = newTokens;
    },
  };
}

function useApiContext(): ApiContextValue {
  const context = use(ApiContext);
  if (!context) {
    throw new Error("Use the API hooks inside an ApiProvider.");
  }
  return context;
}

/** Whether the user has signed in to the current server. */
export function useSignedIn(): boolean {
  return useApiContext().signedIn;
}

/** The URL of the server the app talks to, or null when there is none yet. */
export function useServerUrl(): string | null {
  return useApiContext().serverUrl;
}

/** The API client for the current server, or null when there is none yet. */
export function useOptionalApiClient(): ApiClient | null {
  return useApiContext().client;
}

/** The API client for the current server. Use it only where a server is set (see `useServerUrl`). */
export function useApiClient(): ApiClient {
  const { client } = useApiContext();
  if (!client) {
    throw new Error("No server is set, so there is no API client.");
  }
  return client;
}

/** Stores the tokens from a login, which the API client then sends. */
export function useSaveTokens(): (tokens: StoredTokens) => Promise<void> {
  return useApiContext().saveTokens;
}

/** Stores the URL of the server to talk to from now on, and switches the API client to it. */
export function useSaveServerUrl(): (url: string) => Promise<void> {
  return useApiContext().saveServerUrl;
}
