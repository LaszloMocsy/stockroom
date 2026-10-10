import type { TFunction } from "i18next";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
} from "react-native";

import { useSaveServerUrl } from "@/api/provider";

import { checkServer, ConnectError } from "./check-server";
import { normaliseServerUrl } from "./server-url";

export interface ConnectFormProps {
  /** The address to start with, for example a development server's. */
  initialAddress: string | null;
}

/**
 * Asks for the server's address, checks that a Stockroom server answers there, and stores it (spec 5,
 * flow 1). Storing it switches the app to that server.
 *
 * Not a TanStack Query mutation: one belongs to the query client of the server it started on, which
 * connecting replaces.
 */
export function ConnectForm({ initialAddress }: ConnectFormProps) {
  const { t } = useTranslation();
  const saveServerUrl = useSaveServerUrl();
  const [address, setAddress] = useState(initialAddress ?? "");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<ConnectError | null>(null);

  const submit = async () => {
    if (pending) {
      return;
    }
    setPending(true);
    setError(null);
    try {
      await connect(address, saveServerUrl);
    } catch (caught) {
      // connect throws only ConnectError.
      setError(caught as ConnectError);
    } finally {
      setPending(false);
    }
  };

  const changeAddress = (text: string) => {
    setAddress(text);
    // The error was about the previous address.
    setError(null);
  };

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      contentInsetAdjustmentBehavior="automatic"
      keyboardShouldPersistTaps="handled"
    >
      <Text style={styles.intro}>{t("connect.intro")}</Text>
      <Text style={styles.label}>{t("connect.addressLabel")}</Text>
      <TextInput
        accessibilityLabel={t("connect.addressLabel")}
        autoCapitalize="none"
        autoComplete="url"
        autoCorrect={false}
        editable={!pending}
        inputMode="url"
        onChangeText={changeAddress}
        onSubmitEditing={submit}
        placeholder={t("connect.addressPlaceholder")}
        returnKeyType="go"
        style={styles.input}
        textContentType="URL"
        value={address}
      />
      {error && (
        <Text role="alert" style={styles.error}>
          {errorText(error, t)}
        </Text>
      )}
      <Pressable
        accessibilityState={{
          disabled: pending,
          busy: pending,
        }}
        disabled={pending}
        onPress={submit}
        role="button"
        style={({ pressed }) => [
          styles.button,
          (pressed || pending) && styles.buttonDimmed,
        ]}
      >
        {pending && <ActivityIndicator color="#ffffff" />}
        <Text style={styles.buttonText}>
          {pending ? t("connect.connecting") : t("connect.submit")}
        </Text>
      </Pressable>
    </ScrollView>
  );
}

/**
 * Normalises the address, checks that a Stockroom server answers there, and stores its URL.
 *
 * @throws {ConnectError} When any of these fails.
 */
async function connect(
  address: string,
  saveServerUrl: (url: string) => Promise<void>,
): Promise<void> {
  const url = normaliseServerUrl(address);
  if (!url) {
    throw new ConnectError("malformed");
  }
  await checkServer(url);
  try {
    await saveServerUrl(url);
  } catch {
    throw new ConnectError("save_failed", url);
  }
}

function errorText(error: ConnectError, t: TFunction): string {
  const url = error.url ?? "";
  switch (error.reason) {
    case "malformed":
      return t("connect.errors.malformed");
    case "unreachable":
      return t("connect.errors.unreachable", { url });
    case "server_error":
      return t("connect.errors.serverError", {
        url,
        status: String(error.status),
      });
    case "not_stockroom":
      return t("connect.errors.notStockroom", { url });
    case "save_failed":
      return t("connect.errors.saveFailed");
  }
}

const styles = StyleSheet.create({
  container: {
    gap: 12,
    padding: 24,
  },
  intro: {
    fontSize: 16,
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
  error: {
    color: "#b3261e",
    fontSize: 14,
  },
  button: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    borderRadius: 8,
    backgroundColor: "#0a5cc2",
  },
  buttonDimmed: {
    opacity: 0.7,
  },
  buttonText: {
    color: "#ffffff",
    fontSize: 16,
    fontWeight: "600",
  },
});
