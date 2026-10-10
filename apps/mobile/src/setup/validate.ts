export interface SetupValues {
  displayName: string;
  username: string;
  password: string;
  repeatedPassword: string;
}

export type SetupField = keyof SetupValues;

/** Why a field is not accepted, as a key under `setup.errors` in the translations. */
export type SetupFieldError =
  "required" | "usernameCharacters" | "passwordTooShort" | "passwordsDiffer";

/** The server's minimum (spec 11), checked here too so that the message can be translated. */
export const MinPasswordLength = 8;

export const MaxLengths = {
  displayName: 100,
  username: 64,
  password: 128,
} as const;

/** The characters the server allows in usernames. */
const UsernamePattern = /^[A-Za-z0-9\-._@+]+$/;

/**
 * Checks the setup form before it is sent, returning an error for each field that the server would
 * refuse. Names are trimmed when sent, so only their trimmed value counts.
 */
export function validateSetup(
  values: SetupValues,
): Partial<Record<SetupField, SetupFieldError>> {
  const errors: Partial<Record<SetupField, SetupFieldError>> = {};
  const username = values.username.trim();

  if (!values.displayName.trim()) {
    errors.displayName = "required";
  }
  if (!username) {
    errors.username = "required";
  } else if (!UsernamePattern.test(username)) {
    errors.username = "usernameCharacters";
  }
  if (values.password.length < MinPasswordLength) {
    errors.password = "passwordTooShort";
  }
  if (values.repeatedPassword !== values.password) {
    errors.repeatedPassword = "passwordsDiffer";
  }
  return errors;
}
