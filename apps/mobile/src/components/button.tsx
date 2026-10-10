import { ActivityIndicator, Pressable, StyleSheet, Text } from "react-native";

export interface ButtonProps {
  title: string;
  onPress: () => void;
  /** Shows a spinner and ignores presses while the action it started runs. */
  busy?: boolean;
}

/** The app's primary button: full width, with a large touch target (spec 6). */
export function Button({ title, onPress, busy = false }: ButtonProps) {
  return (
    <Pressable
      accessibilityState={{ disabled: busy, busy }}
      disabled={busy}
      onPress={onPress}
      role="button"
      style={({ pressed }) => [
        styles.button,
        (pressed || busy) && styles.dimmed,
      ]}
    >
      {busy && <ActivityIndicator color="#ffffff" />}
      <Text style={styles.title}>{title}</Text>
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
  dimmed: {
    opacity: 0.7,
  },
  title: {
    color: "#ffffff",
    fontSize: 16,
    fontWeight: "600",
  },
});
