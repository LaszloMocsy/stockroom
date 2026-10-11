import type { TFunction } from "i18next";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ScrollView, StyleSheet, Text } from "react-native";

import { clientVersion } from "@/api/client";
import { useSaveServerUrl } from "@/api/provider";
import { compatibilityWith } from "@/compatibility/compatibility";
import { Button } from "@/components/button";
import { TextField } from "@/components/text-field";

import { checkServer, ConnectError } from "./check-server";
import { isAllowedServerUrl, normaliseServerUrl } from "./server-url";

export interface ConnectFormProps {
  /** The address to start with, for example a development server's. */
  initialAddress: string | null;
  /** Whether to accept `http://` servers other than `localhost`; only development builds do. */
  allowHttp: boolean;
}

/**
 * Asks for the server's address, checks that a Stockroom server answers there, and stores it (spec 5,
 * flow 1). Storing it switches the app to that server.
 *
 * Not a TanStack Query mutation: one belongs to the query client of the server it started on, which
 * connecting replaces.
 */
export function ConnectForm({ initialAddress, allowHttp }: ConnectFormProps) {
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
      await connect(address, allowHttp, saveServerUrl);
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
      <TextField
        autoCapitalize="none"
        autoComplete="url"
        autoCorrect={false}
        editable={!pending}
        error={error && errorText(error, t)}
        inputMode="url"
        label={t("connect.addressLabel")}
        onChangeText={changeAddress}
        onSubmitEditing={() => void submit()}
        placeholder={t("connect.addressPlaceholder")}
        returnKeyType="go"
        textContentType="URL"
        value={address}
      />
      <Button
        busy={pending}
        onPress={() => void submit()}
        title={pending ? t("connect.connecting") : t("connect.submit")}
      />
    </ScrollView>
  );
}

/**
 * Normalises the address, refuses `http://` unless allowed, checks that a Stockroom server answers
 * there and that this app can work with it (spec 10.1), and stores its URL.
 *
 * @throws {ConnectError} When any of these fails.
 */
async function connect(
  address: string,
  allowHttp: boolean,
  saveServerUrl: (url: string) => Promise<void>,
): Promise<void> {
  const url = normaliseServerUrl(address);
  if (!url) {
    throw new ConnectError("malformed");
  }
  if (!isAllowedServerUrl(url, allowHttp)) {
    throw new ConnectError("insecure", url);
  }
  const info = await checkServer(url);
  const compatibility = compatibilityWith(info);
  if (compatibility === null) {
    throw new ConnectError("not_stockroom", url);
  }
  if (compatibility !== "ok") {
    throw new ConnectError(compatibility, url);
  }
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
    case "insecure":
      return t("connect.errors.insecure", { url });
    case "unreachable":
      return t("connect.errors.unreachable", { url });
    case "server_error":
      return t("connect.errors.serverError", {
        url,
        status: String(error.status),
      });
    case "not_stockroom":
      return t("connect.errors.notStockroom", { url });
    case "app_outdated":
      return t("connect.errors.appOutdated", { url, clientVersion });
    case "server_outdated":
      return t("connect.errors.serverOutdated", { url });
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
});
