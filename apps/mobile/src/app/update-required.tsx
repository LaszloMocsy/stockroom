import { Stack } from "expo-router";
import { useTranslation } from "react-i18next";

import { UpdateRequired } from "@/compatibility/update-required";

export default function UpdateRequiredScreen() {
  const { t } = useTranslation();

  return (
    <>
      <Stack.Screen options={{ title: t("compatibility.title") }} />
      <UpdateRequired />
    </>
  );
}
