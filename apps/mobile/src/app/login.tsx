import { Stack } from "expo-router";
import { useTranslation } from "react-i18next";

import { LoginForm } from "@/session/login-form";

export default function LoginScreen() {
  const { t } = useTranslation();

  return (
    <>
      <Stack.Screen options={{ title: t("login.title") }} />
      <LoginForm />
    </>
  );
}
