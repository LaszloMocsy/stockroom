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

import { ApiProvider } from "@/api/provider";
import ConnectScreen from "@/app/connect";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage, type AppStorage } from "@/storage/storage";

import { RootStack } from "./root-stack";

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let storage: AppStorage;

beforeEach(() => {
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(async () =>
    Response.json({
      server_version: "0.1.0",
      api_version: "1.0",
      min_client_version: "0.0.0",
      setup_required: false,
    }),
  );
  storage = createAppStorage(new MemoryStore());
});

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
});
