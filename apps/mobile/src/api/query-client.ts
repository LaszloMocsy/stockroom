import { ApiResponseError } from "@stockroom/api-client";
import { focusManager, QueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { AppState } from "react-native";

const MaxRetries = 3;

/**
 * Retries a failed query up to three times, with TanStack Query's growing delay, unless the server
 * rejected the request (4xx): sending it again would get the same answer.
 */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (
    error instanceof ApiResponseError &&
    error.status >= 400 &&
    error.status < 500
  ) {
    return false;
  }
  return failureCount < MaxRetries;
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: shouldRetry },
    },
  });
}

/**
 * Tells TanStack Query when the app is in the foreground, so stale queries refetch when the user
 * returns to the app, as they do in a browser when its window regains focus.
 */
export function useAppStateFocus() {
  useEffect(() => {
    const subscription = AppState.addEventListener("change", (status) =>
      focusManager.setFocused(status === "active"),
    );
    return () => subscription.remove();
  }, []);
}
