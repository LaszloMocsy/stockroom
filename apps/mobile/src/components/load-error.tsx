import { useTranslation } from "react-i18next";
import { StyleSheet, Text, View } from "react-native";

import { Button } from "./button";

export interface LoadErrorProps {
  /** What could not be loaded, for example "The products could not be loaded." */
  message: string;
  retry: () => void;
  /** Whether the retry is running: shows a spinner and ignores presses. */
  retrying: boolean;
}

/** Says that something failed to load, with a button to try again. */
export function LoadError({ message, retry, retrying }: LoadErrorProps) {
  const { t } = useTranslation();
  return (
    <View style={styles.container}>
      <Text role="alert" style={styles.message}>
        {message}
      </Text>
      <Button
        busy={retrying}
        onPress={retry}
        title={retrying ? t("common.retrying") : t("common.retry")}
        variant="secondary"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 12,
  },
  message: {
    color: "#b3261e",
    fontSize: 16,
  },
});
