import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { fireEvent, render, screen } from "@testing-library/react-native";
import { Text } from "react-native";

import { ApiProvider, useSignedIn } from "@/api/provider";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage, type AppStorage } from "@/storage/storage";

import { LoginForm } from "./login-form";

const tokens = {
  token_type: "Bearer",
  access_token: "access-1",
  expires_in: 900,
  refresh_token: "refresh-1",
};

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let storage: AppStorage;
let loginBodies: unknown[];

beforeEach(async () => {
  loginBodies = [];
  fetch = jest.spyOn(globalThis, "fetch");
  answerLogin(() => Response.json(tokens));
  storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
});

afterEach(() => {
  fetch.mockRestore();
});

/** Answers `POST /api/v1/auth/login` and records what was sent. */
function answerLogin(response: () => Response) {
  fetch.mockImplementation(async (input) => {
    loginBodies.push(await (input as Request).json());
    return response();
  });
}

/** Answers with an API error. */
function apiError(status: number, code: string, headers?: HeadersInit) {
  return () =>
    Response.json(
      { error: { code, message: "Refused.", details: null } },
      { status, ...(headers && { headers }) },
    );
}

/** Shows whether the provider counts the user as signed in. */
function SignedIn() {
  return <Text testID="signed-in">{String(useSignedIn())}</Text>;
}

async function renderForm() {
  await render(
    <ApiProvider storage={storage}>
      <LoginForm />
      <SignedIn />
    </ApiProvider>,
  );
  await screen.findByLabelText("Username");
}

async function signIn(username = "anna", password = "correct horse") {
  await fireEvent.changeText(screen.getByLabelText("Username"), username);
  await fireEvent.changeText(screen.getByLabelText("Password"), password);
  await fireEvent.press(screen.getByRole("button", { name: "Sign in" }));
}

describe("LoginForm", () => {
  it("names the server", async () => {
    await renderForm();

    expect(
      screen.getByText("Sign in to https://stock.example.com."),
    ).toBeOnTheScreen();
  });

  it("stores the tokens and signs in", async () => {
    await renderForm();

    await signIn(" anna ");

    expect(await screen.findByText("true")).toBeOnTheScreen();
    expect(await storage.getTokens()).toEqual({
      accessToken: "access-1",
      refreshToken: "refresh-1",
    });
    expect(loginBodies).toEqual([
      expect.objectContaining({ username: "anna", password: "correct horse" }),
    ]);
  });

  it("requires a username and a password", async () => {
    await renderForm();

    await signIn("  ", "");

    expect(screen.getAllByText("This is required.")).toHaveLength(2);
    expect(fetch).not.toHaveBeenCalled();
  });

  it("says when the credentials are wrong, and clears the password", async () => {
    answerLogin(apiError(401, "invalid_credentials"));
    await renderForm();

    await signIn();

    expect(
      await screen.findByText("The username or password is wrong."),
    ).toBeOnTheScreen();
    expect(screen.getByLabelText("Username")).toHaveDisplayValue("anna");
    expect(screen.getByLabelText("Password")).toHaveDisplayValue("");
    expect(screen.queryByText("This is required.")).not.toBeOnTheScreen();
    expect(screen.getByTestId("signed-in")).toHaveTextContent("false");
    expect(await storage.getTokens()).toBeNull();
  });

  it("says how long a locked account has to wait", async () => {
    answerLogin(apiError(429, "account_locked_out", { "Retry-After": "120" }));
    await renderForm();

    await signIn();

    expect(
      await screen.findByText(
        "Too many wrong passwords. Try again in 2 minutes.",
      ),
    ).toBeOnTheScreen();
  });

  it("rounds a short wait up to a minute", async () => {
    answerLogin(apiError(429, "rate_limited", { "Retry-After": "12" }));
    await renderForm();

    await signIn();

    expect(
      await screen.findByText("Too many attempts. Try again in 1 minute."),
    ).toBeOnTheScreen();
  });

  it("says when the server fails", async () => {
    answerLogin(() => new Response("Bad gateway", { status: 502 }));
    await renderForm();

    await signIn();

    expect(
      await screen.findByText(
        "The server could not sign you in (HTTP 502). Try again.",
      ),
    ).toBeOnTheScreen();
  });

  it("says when the server cannot be reached", async () => {
    fetch.mockRejectedValue(new TypeError("Network request failed"));
    await renderForm();

    await signIn();

    expect(
      await screen.findByText(
        "Cannot reach the server. Check your network connection and try again.",
      ),
    ).toBeOnTheScreen();
  });

  it("clears the error when the user types", async () => {
    answerLogin(apiError(401, "invalid_credentials"));
    await renderForm();
    await signIn();
    await screen.findByRole("alert");

    await fireEvent.changeText(
      screen.getByLabelText("Password"),
      "correct horse battery",
    );

    expect(screen.queryByRole("alert")).not.toBeOnTheScreen();
  });
});
