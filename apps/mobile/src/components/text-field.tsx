import type { Ref } from "react";
import {
  StyleSheet,
  Text,
  TextInput,
  View,
  type TextInputProps,
} from "react-native";

export interface TextFieldProps extends Omit<
  TextInputProps,
  "style" | "accessibilityLabel"
> {
  /** Shown above the input, and its accessible name. */
  label: string;
  /** Shown below the input, for example the rules a value has to follow. */
  hint?: string | undefined;
  /** Why the value is not accepted; shown below the input and announced. */
  error?: string | null | undefined;
  ref?: Ref<TextInput>;
}

/** A labelled text input with an optional hint and error. */
export function TextField({ label, hint, error, ...input }: TextFieldProps) {
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        accessibilityLabel={label}
        aria-invalid={Boolean(error)}
        style={[styles.input, error ? styles.inputInvalid : null]}
        {...input}
      />
      {hint && <Text style={styles.hint}>{hint}</Text>}
      {error && (
        <Text role="alert" style={styles.error}>
          {error}
        </Text>
      )}
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
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: "#8a8a8e",
    borderRadius: 8,
    paddingHorizontal: 12,
    fontSize: 16,
  },
  inputInvalid: {
    borderColor: "#b3261e",
    borderWidth: 2,
  },
  hint: {
    fontSize: 13,
  },
  error: {
    color: "#b3261e",
    fontSize: 14,
  },
});
