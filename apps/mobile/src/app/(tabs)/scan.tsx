import { useTranslation } from "react-i18next";

import { ComingSoon } from "@/components/coming-soon";

export default function ScanScreen() {
  const { t } = useTranslation();

  return <ComingSoon text={t("scan.comingSoon")} />;
}
