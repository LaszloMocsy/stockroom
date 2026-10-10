import { Stack } from "expo-router";
import { useTranslation } from "react-i18next";

import { Settings } from "@/settings/settings";

export default function SettingsScreen() {
  const { t } = useTranslation();

  return (
    <>
      <Stack.Screen options={{ title: t("settings.title") }} />
      <Settings />
    </>
  );
}
