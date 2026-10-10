import { useTranslation } from "react-i18next";
import { StyleSheet, Text } from "react-native";

import { useServerUrl } from "@/api/provider";
import { useServerInfo } from "@/api/server-info";

/** Whether the app reaches the current server, with the server's and API's versions. */
export function ServerStatus() {
  const { t } = useTranslation();
  const serverUrl = useServerUrl();
  if (!serverUrl) {
    return <Text style={styles.text}>{t("serverStatus.noServer")}</Text>;
  }
  return <ServerInfo serverUrl={serverUrl} />;
}

function ServerInfo({ serverUrl }: { serverUrl: string }) {
  const { t } = useTranslation();
  const info = useServerInfo();

  let text: string;
  if (info.data) {
    text = t("serverStatus.connected", {
      serverUrl,
      serverVersion: info.data.server_version,
      apiVersion: info.data.api_version,
    });
  } else if (info.error) {
    text = t("serverStatus.unreachable", {
      serverUrl,
      message: info.error.message,
    });
  } else {
    text = t("serverStatus.connecting", { serverUrl });
  }
  return <Text style={styles.text}>{text}</Text>;
}

const styles = StyleSheet.create({
  text: {
    fontSize: 16,
  },
});
