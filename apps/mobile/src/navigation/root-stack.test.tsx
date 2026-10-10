import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { fireEvent, renderRouter, screen } from "expo-router/testing-library";
import { Text } from "react-native";

import { clientVersion } from "@/api/client";
import { ApiProvider } from "@/api/provider";
import ConnectScreen from "@/app/connect";
import SetupScreen from "@/app/setup";
import UpdateRequiredScreen from "@/app/update-required";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage, type AppStorage } from "@/storage/storage";

import { RootStack } from "./root-stack";

const info = {
  server_version: "0.1.0",
  api_version: "1.0",
  min_client_version: "0.0.0",
  setup_required: false,
};

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let storage: AppStorage;

beforeEach(() => {
  fetch = jest
    .spyOn(globalThis, "fetch")
    .mockImplementation(async () => Response.json(info));
  storage = createAppStorage(new MemoryStore());
});

/** Answers /info with these changes from a compatible server's. */
function serveInfo(changes: Partial<typeof info>) {
  fetch.mockImplementation(async () => Response.json({ ...info, ...changes }));
}

afterEach(() => {
  fetch.mockRestore();
});

/**
 * Renders the app's routes, with the real root stack and Connect screen and a stand-in home screen, and
 * returns a function for the current path.
 */
async function renderApp(): Promise<() => string> {
  const app = renderRouter({
    _layout: () => (
      <ApiProvider storage={storage}>
        <RootStack />
      </ApiProvider>
    ),
    index: () => <Text>Home</Text>,
    connect: ConnectScreen,
    "update-required": UpdateRequiredScreen,
    setup: SetupScreen,
  });
  await app;
  // The provider renders the routes once it has read the stored server.
  await screen.findByText(/Home|Enter the address/);
  // Not app itself: returning a promise from an async function unwraps it, which loses the helpers.
  return () => app.getPathname();
}

describe("RootStack", () => {
  it("opens the Connect screen while no server is stored", async () => {
    const pathname = await renderApp();

    expect(pathname()).toBe("/connect");
    expect(screen.getByLabelText("Server address")).toBeOnTheScreen();
  });

  it("opens the home screen when a server is stored", async () => {
    await storage.setServerUrl("https://stock.example.com");

    const pathname = await renderApp();

    expect(pathname()).toBe("/");
    expect(screen.getByText("Home")).toBeOnTheScreen();
  });

  it("goes to the home screen after connecting", async () => {
    const pathname = await renderApp();

    await fireEvent.changeText(
      screen.getByLabelText("Server address"),
      "stock.example.com",
    );
    await fireEvent.press(screen.getByRole("button", { name: "Connect" }));

    expect(await screen.findByText("Home")).toBeOnTheScreen();
    expect(pathname()).toBe("/");
    expect(await storage.getServerUrl()).toBe("https://stock.example.com");
  });

  it("asks for an app update when the server needs a newer app", async () => {
    await storage.setServerUrl("https://stock.example.com");
    serveInfo({ min_client_version: "99.0.0" });

    const pathname = await renderApp();

    expect(
      await screen.findByText(
        "Please update the app. This server needs a newer version.",
      ),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/update-required");
    expect(
      screen.getByText(`App ${clientVersion}, server 0.1.0 (API 1.0)`),
    ).toBeOnTheScreen();
  });

  it("says when the server is outdated", async () => {
    await storage.setServerUrl("https://stock.example.com");
    serveInfo({ server_version: "0.0.9", api_version: "0.9" });

    const pathname = await renderApp();

    expect(
      await screen.findByText(
        "This server is outdated. Ask your administrator to update it.",
      ),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/update-required");
  });

  it("lets the user in once the server is updated", async () => {
    await storage.setServerUrl("https://stock.example.com");
    serveInfo({ api_version: "0.9" });
    const pathname = await renderApp();
    await screen.findByText(
      "This server is outdated. Ask your administrator to update it.",
    );

    // The screen checks again when it opens.
    const retry = await screen.findByRole("button", { name: "Try again" });

    serveInfo({});
    await fireEvent.press(retry);

    expect(await screen.findByText("Home")).toBeOnTheScreen();
    expect(pathname()).toBe("/");
  });

  it("stays usable while the server cannot be reached", async () => {
    await storage.setServerUrl("https://stock.example.com");
    fetch.mockRejectedValue(new TypeError("Network request failed"));

    const pathname = await renderApp();

    expect(screen.getByText("Home")).toBeOnTheScreen();
    expect(pathname()).toBe("/");
  });

  it("sets up a server without users and signs in", async () => {
    await storage.setServerUrl("https://stock.example.com");
    let hasUsers = false;
    fetch.mockImplementation(async (input) => {
      switch (new URL((input as Request).url).pathname) {
        case "/api/v1/setup":
          hasUsers = true;
          return Response.json(
            {
              id: "6f1c1a52-8a1e-4c63-9a54-1e1f4b8f2d10",
              username: "anna",
              display_name: "Anna",
              role: "ADMIN",
            },
            { status: 201 },
          );
        case "/api/v1/auth/login":
          return Response.json({
            token_type: "Bearer",
            access_token: "access-1",
            expires_in: 900,
            refresh_token: "refresh-1",
          });
        default:
          return Response.json({ ...info, setup_required: !hasUsers });
      }
    });
    const pathname = await renderApp();
    expect(await screen.findByLabelText("Your name")).toBeOnTheScreen();
    expect(pathname()).toBe("/setup");

    await fireEvent.changeText(screen.getByLabelText("Your name"), "Anna");
    await fireEvent.changeText(screen.getByLabelText("Username"), "anna");
    await fireEvent.changeText(
      screen.getByLabelText("Password"),
      "correct horse",
    );
    await fireEvent.changeText(
      screen.getByLabelText("Repeat password"),
      "correct horse",
    );
    await fireEvent.press(
      screen.getByRole("button", { name: "Create account" }),
    );

    expect(await screen.findByText("Home")).toBeOnTheScreen();
    expect(pathname()).toBe("/");
    // Signed in: the requests since carry the new access token.
    const lastRequest = fetch.mock.calls.at(-1)![0] as Request;
    expect(lastRequest.headers.get("Authorization")).toBe("Bearer access-1");
  });
});
