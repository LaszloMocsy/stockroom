import { Stack } from "expo-router";
import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

import { ServerStatus } from "@/components/server-status";

export default function HomeScreen() {
  const { t } = useTranslation();

  return (
    <View style={styles.container}>
      <Stack.Screen options={{ title: t("app.name") }} />
      <Text style={styles.title}>{t("app.name")}</Text>
      <Text style={styles.subtitle}>{t("home.comingSoon")}</Text>
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
