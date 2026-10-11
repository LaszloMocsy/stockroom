import { unwrap } from "@stockroom/api-client";
import { useQuery } from "@tanstack/react-query";

import { useApiClient } from "@/api/provider";

/** The role that may archive products and manage users (spec 2.1). */
const AdminRole = "ADMIN";

/** The signed-in user (`GET /me`): their name, username, and role. */
export function useMe() {
  const client = useApiClient();
  return useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(client.GET("/api/v1/me")),
  });
}

/**
 * Whether the signed-in user is an ADMIN, so that controls only ADMIN users may use are shown. False
 * until that is known. The server checks the role on every request anyway (spec 11).
 */
export function useIsAdmin(): boolean {
  return useMe().data?.role === AdminRole;
}
