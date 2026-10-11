import type { Ref } from "react";
import { useTranslation } from "react-i18next";
import {
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
  type TextInputProps,
} from "react-native";

import { MaxQuantity } from "@/products/validate-product";

import { stepQuantity } from "./quantity";

export interface QuantityStepperProps extends Pick<
  TextInputProps,
  "onSubmitEditing" | "returnKeyType" | "submitBehavior"
> {
  /** Shown above the input, and its accessible name. */
  label: string;
  value: string;
  onChangeText: (text: string) => void;
  /** The smallest quantity the buttons step to. */
  min: number;
  /** Why the value is not accepted; shown below the input and announced. */
  error?: string | undefined;
  editable?: boolean;
  ref?: Ref<TextInput>;
}

/**
 * A whole-number input with buttons that take one off and add one, for one-handed use (spec 6). The
 * number can also be typed; the buttons keep it from `min` to the server's largest quantity.
 */
export function QuantityStepper({
  label,
  value,
  onChangeText,
  min,
  error,
  editable = true,
  ref,
  ...input
}: QuantityStepperProps) {
  const { t } = useTranslation();
  const current = Number(value.trim());
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <View style={styles.row}>
        <StepButton
          disabled={!editable || current <= min}
          label={t("quantity.decrease")}
          onPress={() => onChangeText(stepQuantity(value, -1, min))}
          symbol="−"
        />
        <TextInput
          ref={ref}
          accessibilityLabel={label}
          aria-invalid={Boolean(error)}
          editable={editable}
          keyboardType="number-pad"
          maxLength={String(MaxQuantity).length}
          onChangeText={onChangeText}
          selectTextOnFocus
          style={[styles.input, error ? styles.inputInvalid : null]}
          value={value}
          {...input}
        />
        <StepButton
          disabled={!editable || current >= MaxQuantity}
          label={t("quantity.increase")}
          onPress={() => onChangeText(stepQuantity(value, 1, min))}
          symbol="+"
        />
      </View>
      {error && (
        <Text role="alert" style={styles.error}>
          {error}
        </Text>
      )}
    </View>
  );
}

function StepButton({
  symbol,
  label,
  onPress,
  disabled,
}: {
  symbol: string;
  label: string;
  onPress: () => void;
  disabled: boolean;
}) {
  return (
    <Pressable
      accessibilityLabel={label}
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      role="button"
      style={({ pressed }) => [
        styles.step,
        (pressed || disabled) && styles.dimmed,
      ]}
    >
      <Text style={styles.symbol}>{symbol}</Text>
    </Pressable>
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
  row: {
    flexDirection: "row",
    gap: 8,
  },
  input: {
    flex: 1,
    minHeight: 56,
    borderWidth: 1,
    borderColor: "#8a8a8e",
    borderRadius: 8,
    paddingHorizontal: 12,
    fontSize: 24,
    fontVariant: ["tabular-nums"],
    textAlign: "center",
  },
  inputInvalid: {
    borderColor: "#b3261e",
    borderWidth: 2,
  },
  step: {
    width: 56,
    minHeight: 56,
    alignItems: "center",
    justifyContent: "center",
    borderRadius: 8,
    borderWidth: 2,
    borderColor: "#0a5cc2",
  },
  dimmed: {
    opacity: 0.4,
  },
  symbol: {
    color: "#0a5cc2",
    fontSize: 28,
    fontWeight: "600",
  },
  error: {
    color: "#b3261e",
    fontSize: 14,
  },
});
