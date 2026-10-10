import type { InfoResponse } from "@stockroom/api-client";
import { Stack } from "expo-router";
import { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";

import { createClient, devServerUrl } from "@/api/client";

type ServerState =
  | { status: "loading" }
  | { status: "connected"; info: InfoResponse }
  | { status: "failed"; message: string };

export default function HomeScreen() {
  const [server, setServer] = useState<ServerState>({ status: "loading" });

  useEffect(() => {
    const client = createClient({ baseUrl: devServerUrl });
    client.GET("/api/v1/info").then(
      ({ data, response }) =>
        setServer(
          data
            ? { status: "connected", info: data }
            : { status: "failed", message: `HTTP ${response.status}` },
        ),
      (error: unknown) =>
        setServer({ status: "failed", message: String(error) }),
    );
  }, []);

  return (
    <View style={styles.container}>
      <Stack.Screen options={{ title: "Stockroom" }} />
      <Text style={styles.title}>Stockroom</Text>
      <Text style={styles.subtitle}>Mobile app coming soon.</Text>
      <Text style={styles.server}>{describe(server)}</Text>
    </View>
  );
}

function describe(server: ServerState): string {
  switch (server.status) {
    case "loading":
      return `Connecting to ${devServerUrl}…`;
    case "connected":
      return `Connected to ${devServerUrl}: server ${server.info.server_version}, API ${server.info.api_version}`;
    case "failed":
      return `Cannot reach ${devServerUrl}: ${server.message}`;
  }
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    padding: 24,
  },
  title: {
    fontSize: 28,
    fontWeight: "600",
  },
  subtitle: {
    fontSize: 16,
  },
  server: {
    fontSize: 14,
    textAlign: "center",
  },
});
