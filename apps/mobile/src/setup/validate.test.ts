import { describe, expect, it } from "@jest/globals";

import { validateSetup, type SetupValues } from "./validate";

const valid: SetupValues = {
  displayName: "Anna Admin",
  username: "anna",
  password: "correct horse",
  repeatedPassword: "correct horse",
};

describe("validateSetup", () => {
  it("accepts valid values", () => {
    expect(validateSetup(valid)).toEqual({});
  });

  it.each(["anna.admin", "anna_admin-2", "anna+stock@example.com", " anna "])(
    "accepts the username %j",
    (username) => {
      expect(validateSetup({ ...valid, username })).toEqual({});
    },
  );

  it("requires a name and a username", () => {
    expect(
      validateSetup({ ...valid, displayName: "  ", username: "" }),
    ).toEqual({ displayName: "required", username: "required" });
  });

  it.each(["anna admin", "anna!", "änna"])(
    "refuses the username %j",
    (username) => {
      expect(validateSetup({ ...valid, username })).toEqual({
        username: "usernameCharacters",
      });
    },
  );

  it("requires at least 8 characters in the password", () => {
    expect(
      validateSetup({
        ...valid,
        password: "1234567",
        repeatedPassword: "1234567",
      }),
    ).toEqual({ password: "passwordTooShort" });
    expect(
      validateSetup({
        ...valid,
        password: "12345678",
        repeatedPassword: "12345678",
      }),
    ).toEqual({});
  });

  it("requires the password to be repeated exactly", () => {
    expect(
      validateSetup({ ...valid, repeatedPassword: "correct horse " }),
    ).toEqual({ repeatedPassword: "passwordsDiffer" });
  });
});
