import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { fireEvent, render, screen } from "@testing-library/react-native";

import { ApiProvider } from "@/api/provider";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage, type AppStorage } from "@/storage/storage";

import { SetupForm } from "./setup-form";
import type { SetupValues } from "./validate";

const user = {
  id: "6f1c1a52-8a1e-4c63-9a54-1e1f4b8f2d10",
  username: "anna",
  display_name: "Anna Admin",
  role: "ADMIN",
};

const tokens = {
  token_type: "Bearer",
  access_token: "access-1",
  expires_in: 900,
  refresh_token: "refresh-1",
};

interface SentRequest {
  body: unknown;
}

/** Answers requests by method and path, for example `POST /api/v1/setup`, and records what was sent. */
let routes: Record<string, () => Response>;
let sent: Record<string, SentRequest[]>;
let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let storage: AppStorage;

beforeEach(async () => {
  routes = {
    "POST /api/v1/setup": () => Response.json(user, { status: 201 }),
    "POST /api/v1/auth/login": () => Response.json(tokens),
  };
  sent = {};
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const request = input as Request;
    const route = `${request.method} ${new URL(request.url).pathname}`;
    const text = await request.text();
    (sent[route] ??= []).push({ body: text ? JSON.parse(text) : undefined });
    return routes[route]?.() ?? new Response(null, { status: 404 });
  });
  storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
});

afterEach(() => {
  fetch.mockRestore();
});

async function renderForm() {
  await render(
    <ApiProvider storage={storage}>
      <SetupForm />
    </ApiProvider>,
  );
  await screen.findByLabelText("Your name");
}

async function fillIn({
  displayName = "Anna Admin",
  username = "anna",
  password = "correct horse",
  repeatedPassword = password,
}: Partial<SetupValues> = {}) {
  await fireEvent.changeText(screen.getByLabelText("Your name"), displayName);
  await fireEvent.changeText(screen.getByLabelText("Username"), username);
  await fireEvent.changeText(screen.getByLabelText("Password"), password);
  await fireEvent.changeText(
    screen.getByLabelText("Repeat password"),
    repeatedPassword,
  );
}

async function submit() {
  await fireEvent.press(screen.getByRole("button", { name: "Create account" }));
}

/** Answers a request with an API error. */
function apiError(status: number, code: string, details: unknown = null) {
  return () =>
    Response.json(
      { error: { code, message: "Refused.", details } },
      { status },
    );
}

describe("SetupForm", () => {
  it("creates the ADMIN and signs in with the same credentials", async () => {
    await renderForm();
    await fillIn({ displayName: " Anna Admin ", username: " anna " });

    await submit();
    await screen.findByRole("button", { name: "Create account" });

    expect(await storage.getTokens()).toEqual({
      accessToken: "access-1",
      refreshToken: "refresh-1",
    });
    expect(sent["POST /api/v1/setup"]).toEqual([
      {
        body: {
          username: "anna",
          display_name: "Anna Admin",
          password: "correct horse",
        },
      },
    ]);
    expect(sent["POST /api/v1/auth/login"]).toEqual([
      {
        body: expect.objectContaining({
          username: "anna",
          password: "correct horse",
        }),
      },
    ]);
  });

  it("checks the fields before sending them", async () => {
    await renderForm();
    await fillIn({
      displayName: "",
      username: "anna admin",
      password: "short",
      repeatedPassword: "shorter",
    });

    await submit();

    expect(screen.getByText("This is required.")).toBeOnTheScreen();
    expect(
      screen.getByText("Use only letters, digits, and - . _ @ +"),
    ).toBeOnTheScreen();
    expect(screen.getByText("Use at least 8 characters.")).toBeOnTheScreen();
    expect(screen.getByText("The passwords do not match.")).toBeOnTheScreen();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("does not show field errors before the first attempt", async () => {
    await renderForm();

    await fillIn({ password: "short" });

    expect(screen.queryByRole("alert")).not.toBeOnTheScreen();
  });

  it("shows the server's errors next to their fields", async () => {
    routes["POST /api/v1/setup"] = apiError(400, "validation_failed", {
      fields: { username: ["Username 'anna' is invalid."] },
    });
    await renderForm();
    await fillIn();

    await submit();

    expect(
      await screen.findByText("Username 'anna' is invalid."),
    ).toBeOnTheScreen();
    expect(sent["POST /api/v1/auth/login"]).toBeUndefined();
    expect(await storage.getTokens()).toBeNull();
  });

  it("says when another device completed setup first", async () => {
    routes["POST /api/v1/setup"] = apiError(409, "setup_already_completed");
    await renderForm();
    await fillIn();

    await submit();

    expect(
      await screen.findByText("This server has already been set up."),
    ).toBeOnTheScreen();
  });

  it("says when there were too many attempts", async () => {
    routes["POST /api/v1/setup"] = apiError(429, "rate_limited");
    await renderForm();
    await fillIn();

    await submit();

    expect(
      await screen.findByText(
        "Too many attempts. Wait a minute and try again.",
      ),
    ).toBeOnTheScreen();
  });

  it("says when the server cannot be reached", async () => {
    fetch.mockRejectedValue(new TypeError("Network request failed"));
    await renderForm();
    await fillIn();

    await submit();

    expect(
      await screen.findByText(
        "Cannot reach the server. Check your network connection and try again.",
      ),
    ).toBeOnTheScreen();
  });

  it("finishes setup even when signing in fails", async () => {
    routes["POST /api/v1/auth/login"] = apiError(429, "rate_limited");
    await renderForm();
    await fillIn();

    await submit();
    await screen.findByRole("button", { name: "Create account" });

    expect(sent["POST /api/v1/setup"]).toHaveLength(1);
    expect(screen.queryByRole("alert")).not.toBeOnTheScreen();
    expect(await storage.getTokens()).toBeNull();
  });
});
