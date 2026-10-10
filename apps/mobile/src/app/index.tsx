import { Stack } from "expo-router";
import { StyleSheet, Text, View } from "react-native";

import { ServerStatus } from "@/components/server-status";

export default function HomeScreen() {
  return (
    <View style={styles.container}>
      <Stack.Screen options={{ title: "Stockroom" }} />
      <Text style={styles.title}>Stockroom</Text>
      <Text style={styles.subtitle}>Mobile app coming soon.</Text>
      <ServerStatus />
    </View>
  );
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
});
