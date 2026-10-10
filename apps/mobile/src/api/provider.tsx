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

import type { AppStorage } from "@/storage/storage";

import { createClient } from "./client";
import { createQueryClient } from "./query-client";

interface ApiContextValue {
  serverUrl: string | null;
  client: ApiClient | null;
  saveServerUrl: (url: string) => Promise<void>;
}

const ApiContext = createContext<ApiContextValue | null>(null);

export interface ApiProviderProps {
  storage: AppStorage;
  children: ReactNode;
}

/**
 * Provides an API client for the stored server and a TanStack Query cache for it. Renders nothing until
 * it has read the stored server URL.
 */
export function ApiProvider({ storage, children }: ApiProviderProps) {
  // Undefined while the stored URL is being read.
  const [serverUrl, setServerUrl] = useState<string | null>();

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

  // A new cache for each server, so one server's data never shows for another.
  const api = useMemo(
    () =>
      serverUrl === undefined
        ? null
        : {
            context: {
              serverUrl,
              client:
                serverUrl === null
                  ? null
                  : createClient({ baseUrl: serverUrl }),
              saveServerUrl,
            },
            queryClient: createQueryClient(),
          },
    [serverUrl, saveServerUrl],
  );

  // Drops a cache as soon as it is replaced or unmounted, rather than leaving its queries to garbage
  // collection, whose five-minute timers would also keep a test run alive. React cleans up this effect
  // before the queries below unsubscribe, which schedules that collection again, so clear afterwards.
  const queryClient = api?.queryClient;
  useEffect(
    () => () => {
      if (queryClient) {
        queueMicrotask(() => queryClient.clear());
      }
    },
    [queryClient],
  );

  if (!api) {
    return null;
  }
  return (
    <ApiContext value={api.context}>
      <QueryClientProvider client={api.queryClient}>
        {children}
      </QueryClientProvider>
    </ApiContext>
  );
}

function useApiContext(): ApiContextValue {
  const context = use(ApiContext);
  if (!context) {
    throw new Error("Use the API hooks inside an ApiProvider.");
  }
  return context;
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

/** Stores the URL of the server to talk to from now on, and switches the API client to it. */
export function useSaveServerUrl(): (url: string) => Promise<void> {
  return useApiContext().saveServerUrl;
}
