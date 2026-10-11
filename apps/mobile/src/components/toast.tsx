import {
  createContext,
  use,
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
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

export interface ToastControls {
  /** Shows a toast in place of the current one; screen readers announce its message. */
  show: (options: ToastOptions) => void;
  hide: () => void;
}

/** How long a toast shows: longer with an action, so there is time to reach it. */
const DurationMs = 4000;
const DurationWithActionMs = 8000;

const ToastContext = createContext<ToastControls | null>(null);

/**
 * Shows short messages at the bottom of the screen, above whichever screen is in front, which hide
 * themselves after a few seconds. As the toast belongs to the app rather than a screen, a screen can
 * show one as it closes, for example a sheet confirming what it saved.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
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
  const controls = useMemo(() => ({ show, hide }), [show, hide]);

  return (
    <ToastContext value={controls}>
      {children}
      {toast && <Toast hide={hide} toast={toast} />}
    </ToastContext>
  );
}

/** Shows and hides the app's toast. Use it inside a `ToastProvider`. */
export function useToast(): ToastControls {
  const controls = use(ToastContext);
  if (!controls) {
    throw new Error("Use useToast inside a ToastProvider.");
  }
  return controls;
}

function Toast({ toast, hide }: { toast: ToastOptions; hide: () => void }) {
  // Expo Router provides the insets; without them, as in some tests, the toast sits above the edge.
  const bottomInset = use(SafeAreaInsetsContext)?.bottom ?? 0;
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
