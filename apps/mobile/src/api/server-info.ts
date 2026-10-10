import { unwrap } from "@stockroom/api-client";
import { skipToken, useQuery } from "@tanstack/react-query";

import { useOptionalApiClient } from "./provider";

/**
 * The current server's `GET /api/v1/info`, shared by every screen that needs it. Like any query, it
 * refetches when the app returns to the foreground, so a server update is noticed. Idle while no server
 * is set.
 */
export function useServerInfo() {
  const client = useOptionalApiClient();
  return useQuery({
    queryKey: ["info"],
    queryFn: client ? () => unwrap(client.GET("/api/v1/info")) : skipToken,
  });
}
