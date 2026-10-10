import { Stack } from "expo-router";
import { useTranslation } from "react-i18next";

import { SetupForm } from "@/setup/setup-form";

export default function SetupScreen() {
  const { t } = useTranslation();

  return (
    <>
      <Stack.Screen options={{ title: t("setup.title") }} />
      <SetupForm />
    </>
  );
}
