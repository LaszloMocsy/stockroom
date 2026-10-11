import { ActivityIndicator, Pressable, StyleSheet, Text } from "react-native";

export interface ButtonProps {
  title: string;
  onPress: () => void;
  /** Shows a spinner and ignores presses while the action it started runs. */
  busy?: boolean;
  /** `primary` for a screen's main action, `secondary` for the others. */
  variant?: "primary" | "secondary";
}

/** The app's button: full width, with a large touch target (spec 6). */
export function Button({
  title,
  onPress,
  busy = false,
  variant = "primary",
}: ButtonProps) {
  const secondary = variant === "secondary";
  return (
    <Pressable
      accessibilityState={{ disabled: busy, busy }}
      disabled={busy}
      onPress={onPress}
      role="button"
      style={({ pressed }) => [
        styles.button,
        secondary && styles.secondaryButton,
        (pressed || busy) && styles.dimmed,
      ]}
    >
      {busy && <ActivityIndicator color={secondary ? "#0a5cc2" : "#ffffff"} />}
      <Text style={[styles.title, secondary && styles.secondaryTitle]}>
        {title}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    borderRadius: 8,
    backgroundColor: "#0a5cc2",
  },
  secondaryButton: {
    borderWidth: 2,
    borderColor: "#0a5cc2",
    backgroundColor: "transparent",
  },
  dimmed: {
    opacity: 0.7,
  },
  title: {
    color: "#ffffff",
    fontSize: 16,
    fontWeight: "600",
  },
  secondaryTitle: {
    color: "#0a5cc2",
  },
});
