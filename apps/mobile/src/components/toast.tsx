import { use, useCallback, useEffect, useState } from "react";
import {
  AccessibilityInfo,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { SafeAreaInsetsContext } from "react-native-safe-area-context";

export interface ToastOptions {
  message: string;
  /** A button next to the message, such as "Undo". Pressing it hides the toast. */
  action?: { label: string; onPress: () => void };
  /** Keeps the toast until another replaces it, for example while the action it offered runs. */
  persistent?: boolean;
}

interface ShownToast extends ToastOptions {
  /** Tells toasts with the same text apart, so that showing one again restarts its time. */
  id: number;
}

/** How long a toast shows: longer with an action, so there is time to reach it. */
const DurationMs = 4000;
const DurationWithActionMs = 8000;

/**
 * A short message at the bottom of the screen, which hides itself after a few seconds (see `Toast`).
 * `show` replaces the current toast, and screen readers announce the message.
 */
export function useToast() {
  const [toast, setToast] = useState<ShownToast | null>(null);

  useEffect(() => {
    if (!toast || toast.persistent) {
      return;
    }
    const timer = setTimeout(
      () => setToast(null),
      toast.action ? DurationWithActionMs : DurationMs,
    );
    return () => clearTimeout(timer);
  }, [toast]);

  const show = useCallback((options: ToastOptions) => {
    setToast((current) => ({ ...options, id: (current?.id ?? 0) + 1 }));
    AccessibilityInfo.announceForAccessibility(options.message);
  }, []);
  const hide = useCallback(() => setToast(null), []);

  return { toast, show, hide };
}

export type ShowToast = (options: ToastOptions) => void;

/**
 * Shows the toast from `useToast`, if any, above the bottom of the screen. Render it after the
 * screen's content, so that it is drawn on top.
 */
export function Toast({
  toast,
  hide,
}: {
  toast: ToastOptions | null;
  hide: () => void;
}) {
  // Expo Router provides the insets; without them, as in some tests, the toast sits above the edge.
  const bottomInset = use(SafeAreaInsetsContext)?.bottom ?? 0;
  if (!toast) {
    return null;
  }
  const { action } = toast;
  return (
    <View
      pointerEvents="box-none"
      style={[styles.position, { bottom: bottomInset + 16 }]}
    >
      <View style={styles.toast} testID="toast">
        <Text style={styles.message}>{toast.message}</Text>
        {action && (
          <Pressable
            hitSlop={8}
            onPress={() => {
              hide();
              action.onPress();
            }}
            role="button"
            style={({ pressed }) => [styles.action, pressed && styles.pressed]}
          >
            <Text style={styles.actionLabel}>{action.label}</Text>
          </Pressable>
        )}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  position: {
    position: "absolute",
    left: 16,
    right: 16,
  },
  toast: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    paddingVertical: 8,
    paddingLeft: 16,
    paddingRight: 8,
    borderRadius: 8,
    backgroundColor: "#1f1f1f",
  },
  message: {
    flex: 1,
    color: "#ffffff",
    fontSize: 16,
  },
  action: {
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: 12,
    borderRadius: 6,
  },
  pressed: {
    opacity: 0.7,
  },
  actionLabel: {
    color: "#8ab4f8",
    fontSize: 16,
    fontWeight: "600",
  },
});
