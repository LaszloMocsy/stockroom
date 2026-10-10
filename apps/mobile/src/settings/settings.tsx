import { unwrap } from "@stockroom/api-client";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Alert, ScrollView, StyleSheet, Text, View } from "react-native";

import {
  useApiClient,
  useChangeServer,
  useServerUrl,
  useSignOut,
} from "@/api/provider";
import { Button } from "@/components/button";

type Action = "signOut" | "changeServer";

/**
 * The server and the signed-in user, with signing out and changing the server (spec 4.7). Either one
 * leaves this screen: the app moves on to the login or the Connect screen.
 */
export function Settings() {
  const { t } = useTranslation();
  const serverUrl = useServerUrl() ?? "";
  const client = useApiClient();
  const signOut = useSignOut();
  const changeServer = useChangeServer();
  const me = useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(client.GET("/api/v1/me")),
  });
  const [pending, setPending] = useState<Action | null>(null);

  // Neither action rejects: both sign out even when the server cannot be reached.
  const run = (action: Action, perform: () => Promise<void>) => {
    if (!pending) {
      setPending(action);
      void perform();
    }
  };

  const confirmChangeServer = () =>
    Alert.alert(
      t("settings.changeServerTitle"),
      t("settings.changeServerMessage", { serverUrl }),
      [
        { text: t("common.cancel"), style: "cancel" },
        {
          text: t("settings.changeServer"),
          style: "destructive",
          onPress: () => run("changeServer", changeServer),
        },
      ],
    );

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      contentInsetAdjustmentBehavior="automatic"
    >
      <View style={styles.section}>
        <Text role="heading" style={styles.heading}>
          {t("settings.accountHeading")}
        </Text>
        {me.data && (
          <Text style={styles.text}>
            {t("settings.signedInAs", {
              name: me.data.display_name,
              username: me.data.username,
            })}
          </Text>
        )}
        <Button
          busy={pending === "signOut"}
          onPress={() => run("signOut", signOut)}
          title={
            pending === "signOut"
              ? t("settings.signingOut")
              : t("settings.signOut")
          }
          variant="secondary"
        />
      </View>
      <View style={styles.section}>
        <Text role="heading" style={styles.heading}>
          {t("settings.serverHeading")}
        </Text>
        <Text style={styles.text}>{serverUrl}</Text>
        <Button
          busy={pending === "changeServer"}
          onPress={confirmChangeServer}
          title={
            pending === "changeServer"
              ? t("settings.changingServer")
              : t("settings.changeServer")
          }
          variant="secondary"
        />
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 32,
    padding: 24,
  },
  section: {
    gap: 12,
  },
  heading: {
    fontSize: 20,
    fontWeight: "600",
  },
  text: {
    fontSize: 16,
  },
});
