import { ApiResponseError, unwrap } from "@stockroom/api-client";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ScrollView, StyleSheet, Text, type TextInput } from "react-native";

import { useApiClient } from "@/api/provider";
import { Button } from "@/components/button";
import { TextField } from "@/components/text-field";
import { useLogIn } from "@/session/log-in";

import {
  MaxLengths,
  MinPasswordLength,
  validateSetup,
  type SetupField,
  type SetupValues,
} from "./validate";

/** The API's error code when another device completed setup first. */
const SetupAlreadyCompleted = "setup_already_completed";

const emptyValues: SetupValues = {
  displayName: "",
  username: "",
  password: "",
  repeatedPassword: "",
};

/**
 * First-run setup (spec 4.7): creates the server's first ADMIN account and signs in with it. `/setup`
 * returns the new user rather than tokens, so the form logs in with the same credentials afterwards.
 * Once the server reports that setup is no longer required, the app moves on from this screen.
 */
export function SetupForm() {
  const { t } = useTranslation();
  const client = useApiClient();
  const logIn = useLogIn();
  const queryClient = useQueryClient();

  const [values, setValues] = useState(emptyValues);
  // Field errors show from the first attempt to submit, not while the user is still typing.
  const [submitted, setSubmitted] = useState(false);
  const usernameInput = useRef<TextInput>(null);
  const passwordInput = useRef<TextInput>(null);
  const repeatedPasswordInput = useRef<TextInput>(null);

  const refreshInfo = () =>
    queryClient.invalidateQueries({ queryKey: ["info"] });

  const setup = useMutation({
    mutationFn: async ({ displayName, username, password }: SetupValues) => {
      const credentials = { username: username.trim(), password };
      await unwrap(
        client.POST("/api/v1/setup", {
          body: {
            username: credentials.username,
            display_name: displayName.trim(),
            password,
          },
        }),
      );
      try {
        await logIn(credentials);
      } catch {
        // The account exists, so setup is over either way; the user signs in on the next screen.
      }
      // Waited for, so the form stays busy until the app moves on.
      await refreshInfo();
    },
    onError: (error) => {
      if (isAlreadyCompleted(error)) {
        void refreshInfo();
      }
    },
  });

  const clientErrors = validateSetup(values);
  const serverErrors = setup.error ? serverFieldErrors(setup.error) : {};
  const fieldError = (field: SetupField): string | undefined => {
    const clientError = submitted ? clientErrors[field] : undefined;
    return clientError
      ? t(`setup.errors.${clientError}`, { min: MinPasswordLength })
      : serverErrors[field];
  };
  const formError = setup.error ? formErrorText(setup.error, t) : null;

  const change = (field: SetupField) => (text: string) => {
    setValues((current) => ({ ...current, [field]: text }));
    // The server's errors were about the previous values.
    if (setup.isError) {
      setup.reset();
    }
  };

  const submit = () => {
    setSubmitted(true);
    if (Object.keys(clientErrors).length === 0 && !setup.isPending) {
      setup.mutate(values);
    }
  };

  return (
    <ScrollView
      automaticallyAdjustKeyboardInsets
      contentContainerStyle={styles.container}
      contentInsetAdjustmentBehavior="automatic"
      keyboardShouldPersistTaps="handled"
    >
      <Text style={styles.intro}>{t("setup.intro")}</Text>
      <TextField
        autoComplete="name"
        editable={!setup.isPending}
        error={fieldError("displayName")}
        hint={t("setup.displayNameHint")}
        label={t("setup.displayNameLabel")}
        maxLength={MaxLengths.displayName}
        onChangeText={change("displayName")}
        onSubmitEditing={() => usernameInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        textContentType="name"
        value={values.displayName}
      />
      <TextField
        ref={usernameInput}
        autoCapitalize="none"
        autoComplete="username"
        autoCorrect={false}
        editable={!setup.isPending}
        error={fieldError("username")}
        hint={t("setup.usernameHint")}
        label={t("setup.usernameLabel")}
        maxLength={MaxLengths.username}
        onChangeText={change("username")}
        onSubmitEditing={() => passwordInput.current?.focus()}
        returnKeyType="next"
        submitBehavior="submit"
        textContentType="username"
        value={values.username}
      />
      <TextField
        ref={passwordInput}
        autoCapitalize="none"
        autoComplete="new-password"
        autoCorrect={false}
        editable={!setup.isPending}
        error={fieldError("password")}
        hint={t("setup.passwordHint", { min: MinPasswordLength })}
        label={t("setup.passwordLabel")}
        maxLength={MaxLengths.password}
        onChangeText={change("password")}
        onSubmitEditing={() => repeatedPasswordInput.current?.focus()}
        returnKeyType="next"
        secureTextEntry
        submitBehavior="submit"
        textContentType="newPassword"
        value={values.password}
      />
      <TextField
        ref={repeatedPasswordInput}
        autoCapitalize="none"
        autoComplete="new-password"
        autoCorrect={false}
        editable={!setup.isPending}
        error={fieldError("repeatedPassword")}
        label={t("setup.repeatedPasswordLabel")}
        maxLength={MaxLengths.password}
        onChangeText={change("repeatedPassword")}
        onSubmitEditing={submit}
        returnKeyType="done"
        secureTextEntry
        textContentType="newPassword"
        value={values.repeatedPassword}
      />
      {formError && (
        <Text role="alert" style={styles.error}>
          {formError}
        </Text>
      )}
      <Button
        busy={setup.isPending}
        onPress={submit}
        title={setup.isPending ? t("setup.submitting") : t("setup.submit")}
      />
    </ScrollView>
  );
}

function isAlreadyCompleted(error: unknown): boolean {
  return (
    error instanceof ApiResponseError && error.code === SetupAlreadyCompleted
  );
}

/** The API's field names in `validation_failed` details, by form field. */
const ServerFieldNames: Record<string, SetupField> = {
  display_name: "displayName",
  username: "username",
  password: "password",
};

/**
 * The server's messages for the fields it refused, from `details.fields` of a `validation_failed` error.
 * They are the server's English text: the form checks the common cases itself, in the user's language.
 */
function serverFieldErrors(error: Error): Partial<Record<SetupField, string>> {
  if (
    !(error instanceof ApiResponseError) ||
    error.code !== "validation_failed"
  ) {
    return {};
  }
  const { details } = error;
  const fields =
    typeof details === "object" && details !== null && "fields" in details
      ? details.fields
      : null;
  if (typeof fields !== "object" || fields === null) {
    return {};
  }

  const errors: Partial<Record<SetupField, string>> = {};
  for (const [name, messages] of Object.entries(fields)) {
    const field = ServerFieldNames[name];
    if (field && Array.isArray(messages) && typeof messages[0] === "string") {
      errors[field] = messages[0];
    }
  }
  return errors;
}

/** The error to show for the whole form, or null when the fields show it. */
function formErrorText(error: Error, t: TFunction): string | null {
  if (!(error instanceof ApiResponseError)) {
    return t("setup.errors.unreachable");
  }
  if (isAlreadyCompleted(error)) {
    return t("setup.errors.alreadyCompleted");
  }
  if (error.status === 429) {
    return t("setup.errors.rateLimited");
  }
  if (Object.keys(serverFieldErrors(error)).length > 0) {
    return null;
  }
  return t("setup.errors.serverError", { status: String(error.status) });
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
