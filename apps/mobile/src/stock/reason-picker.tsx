import type { Schema } from "@stockroom/api-client";
import { useTranslation } from "react-i18next";
import { Pressable, StyleSheet, Text, View } from "react-native";

export type Reason = Schema<"StockMovementReason">;

export interface ReasonPickerProps<R extends Reason> {
  label: string;
  /** The reasons to offer, in order. */
  reasons: readonly R[];
  value: R;
  onChange: (reason: R) => void;
  disabled?: boolean;
}

/**
 * Picks why stock moved (spec 3.1) from `reasons`, as a group of options of which one is chosen. The
 * chosen one is marked by a check mark and its fill, not by colour alone (spec 6).
 */
export function ReasonPicker<R extends Reason>({
  label,
  reasons,
  value,
  onChange,
  disabled = false,
}: ReasonPickerProps<R>) {
  const { t } = useTranslation();
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <View accessibilityLabel={label} role="radiogroup" style={styles.options}>
        {reasons.map((reason) => {
          const checked = reason === value;
          const name = t(`movement.reasons.${reason}`);
          return (
            <Pressable
              key={reason}
              // Without the check mark, which the checked state already tells screen readers.
              accessibilityLabel={name}
              accessibilityState={{ checked, disabled }}
              aria-checked={checked}
              disabled={disabled}
              onPress={() => onChange(reason)}
              role="radio"
              style={({ pressed }) => [
                styles.option,
                checked && styles.checked,
                (pressed || disabled) && styles.dimmed,
              ]}
            >
              <Text style={[styles.text, checked && styles.checkedText]}>
                {checked && "✓ "}
                {name}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  field: {
    gap: 6,
  },
  label: {
    fontSize: 14,
    fontWeight: "600",
  },
  options: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  option: {
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: 14,
    borderRadius: 22,
    borderWidth: 1,
    borderColor: "#8a8a8e",
  },
  checked: {
    borderColor: "#0a5cc2",
    backgroundColor: "#0a5cc2",
  },
  dimmed: {
    opacity: 0.7,
  },
  text: {
    fontSize: 16,
  },
  checkedText: {
    color: "#ffffff",
    fontWeight: "600",
  },
});
