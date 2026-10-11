import { ApiResponseError } from "@stockroom/api-client";
import { useMutation } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ScrollView, StyleSheet, Text, type TextInput } from "react-native";

import { useChangeServer, useServerUrl } from "@/api/provider";
import { Button } from "@/components/button";
import { TextField } from "@/components/text-field";

import { useLogIn, type Credentials } from "./log-in";

/** The server keeps up to this many characters of a username or password. */
const MaxUsernameLength = 64;
const MaxPasswordLength = 128;

/** Signs in with a username and password (spec 4.7). Once signed in, the app moves on from this screen. */
export function LoginForm() {
  const { t } = useTranslation();
  const serverUrl = useServerUrl();
  const logIn = useLogIn();
  const changeServer = useChangeServer();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  // Field errors show from the first attempt to sign in, not while the user is still typing.
  const [submitted, setSubmitted] = useState(false);
  const passwordInput = useRef<TextInput>(null);

  const login = useMutation({
    mutationFn: (credentials: Credentials) => logIn(credentials),
    onError: (error) => {
      // Keep the username, which is the likelier one to be right, and let the user type the password
      // again, without calling the emptied field an error.
      if (error instanceof ApiResponseError && error.status === 401) {
        setPassword("");
        setSubmitted(false);
      }
    },
  });

  const trimmedUsername = username.trim();
  const required = (value: string) =>
    submitted && !value ? t("login.errors.required") : undefined;

  const change = (setValue: (text: string) => void) => (text: string) => {
    setValue(text);
    // The error was about the previous values.
    if (login.isError) {
      login.reset();
    }
  };

  const submit = () => {
    setSubmitted(true);
    if (trimmedUsername && password && !login.isPending) {
      login.mutate({ username: trimmedUsername, password });
    }
  };

  return (
    <ScrollView
      automaticallyAdjustKeyboardInsets
      contentContainerStyle={styles.container}
      contentInsetAdjustmentBehavior="automatic"
      keyboardShouldPersistTaps="handled"
    >
      <Text style={styles.intro}>
        {t("login.intro", { serverUrl: serverUrl ?? "" })}
      </Text>
      <TextField
        autoCapitalize="none"
        autoComplete="username"
        autoCorrect={false}
        editable={!login.isPending}
        error={required(trimmedUsername)}
        label={t("login.usernameLabel")}
        maxLength={MaxUsernameLength}
        onChangeText={change(setUsername)}
        onSubmitEditing={() => passwordInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        textContentType="username"
        value={username}
      />
      <TextField
        ref={passwordInput}
        autoCapitalize="none"
        autoComplete="current-password"
        autoCorrect={false}
        editable={!login.isPending}
        error={required(password)}
        label={t("login.passwordLabel")}
        maxLength={MaxPasswordLength}
        onChangeText={change(setPassword)}
        onSubmitEditing={submit}
        returnKeyType="go"
        secureTextEntry
        textContentType="password"
        value={password}
      />
      {login.error && (
        <Text role="alert" style={styles.error}>
          {errorText(login.error, t)}
        </Text>
      )}
      <Button
        busy={login.isPending}
        onPress={submit}
        title={login.isPending ? t("login.submitting") : t("login.submit")}
      />
      <Button
        onPress={() => void changeServer()}
        title={t("login.changeServer")}
        variant="secondary"
      />
    </ScrollView>
  );
}

/** Whole minutes to wait, at least one: lockouts last minutes, and so does the rate limit's window. */
function minutesToWait(error: ApiResponseError): number {
  return Math.max(1, Math.ceil((error.retryAfter ?? 0) / 60));
}

function errorText(error: Error, t: TFunction): string {
  if (!(error instanceof ApiResponseError)) {
    return t("login.errors.unreachable");
  }
  if (error.status === 401) {
    return t("login.errors.invalidCredentials");
  }
  if (error.code === "account_locked_out") {
    return t("login.errors.lockedOut", { count: minutesToWait(error) });
  }
  if (error.status === 429) {
    return t("login.errors.rateLimited", { count: minutesToWait(error) });
  }
  return t("login.errors.serverError", { status: String(error.status) });
}

const styles = StyleSheet.create({
  container: {
    gap: 16,
    padding: 24,
  },
  intro: {
    fontSize: 16,
  },
  error: {
    color: "#b3261e",
    fontSize: 14,
  },
});
