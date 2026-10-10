import { unwrap } from "@stockroom/api-client";
import { useQuery } from "@tanstack/react-query";
import { StyleSheet, Text } from "react-native";

import { useApiClient, useServerUrl } from "@/api/provider";

/** Whether the app reaches the current server, with the server's and API's versions. */
export function ServerStatus() {
  const serverUrl = useServerUrl();
  if (!serverUrl) {
    return <Text style={styles.text}>No server is set.</Text>;
  }
  return <ServerInfo serverUrl={serverUrl} />;
}

function ServerInfo({ serverUrl }: { serverUrl: string }) {
  const client = useApiClient();
  const info = useQuery({
    queryKey: ["info"],
    queryFn: () => unwrap(client.GET("/api/v1/info")),
  });

  let text: string;
  if (info.data) {
    text = `Connected to ${serverUrl}: server ${info.data.server_version}, API ${info.data.api_version}`;
  } else if (info.error) {
    text = `Cannot reach ${serverUrl}: ${info.error.message}`;
  } else {
    text = `Connecting to ${serverUrl}…`;
  }
  return <Text style={styles.text}>{text}</Text>;
}

const styles = StyleSheet.create({
  text: {
    fontSize: 14,
    textAlign: "center",
  },
});
