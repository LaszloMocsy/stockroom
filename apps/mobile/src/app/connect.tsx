import { Stack } from "expo-router";
import { useTranslation } from "react-i18next";

import { defaultServerUrl } from "@/api/client";
import { ConnectForm } from "@/connect/connect-form";

export default function ConnectScreen() {
  const { t } = useTranslation();

  return (
    <>
      <Stack.Screen options={{ title: t("connect.title") }} />
      <ConnectForm initialAddress={defaultServerUrl} allowHttp={__DEV__} />
    </>
  );
}
