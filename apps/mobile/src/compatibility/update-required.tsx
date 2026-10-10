import { useTranslation } from "react-i18next";
import { ScrollView, StyleSheet, Text } from "react-native";

import { clientVersion } from "@/api/client";
import { useServerInfo } from "@/api/server-info";
import { Button } from "@/components/button";

import { compatibilityWith } from "./compatibility";

/**
 * Tells the user that the app and the server cannot work together, and who has to update which (spec
 * 10.1). Checking again after the update lets the user in.
 */
export function UpdateRequired() {
  const { t } = useTranslation();
  const info = useServerInfo();
  const compatibility = info.data ? compatibilityWith(info.data) : null;

  // The app leaves this screen once the server is compatible again.
  if (
    !info.data ||
    (compatibility !== "app_outdated" && compatibility !== "server_outdated")
  ) {
    return null;
  }

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      contentInsetAdjustmentBehavior="automatic"
    >
      <Text role="alert" style={styles.message}>
        {compatibility === "app_outdated"
          ? t("compatibility.appOutdated")
          : t("compatibility.serverOutdated")}
      </Text>
      <Text style={styles.versions}>
        {t("compatibility.versions", {
          clientVersion,
          serverVersion: info.data.server_version,
          apiVersion: info.data.api_version,
        })}
      </Text>
      <Button
        busy={info.isFetching}
        onPress={() => void info.refetch()}
        title={
          info.isFetching
            ? t("compatibility.checking")
            : t("compatibility.retry")
        }
      />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 12,
    padding: 24,
  },
  message: {
    fontSize: 18,
    fontWeight: "600",
  },
  versions: {
    fontSize: 14,
  },
});
