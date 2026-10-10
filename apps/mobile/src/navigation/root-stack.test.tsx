import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { unwrap } from "@stockroom/api-client";
import { useQuery } from "@tanstack/react-query";
import {
  act,
  fireEvent,
  renderRouter,
  screen,
} from "expo-router/testing-library";
import { Alert, Text } from "react-native";

import { clientVersion } from "@/api/client";
import { ApiProvider, useApiClient } from "@/api/provider";
import ConnectScreen from "@/app/connect";
import LoginScreen from "@/app/login";
import ProductScreen from "@/app/product/[id]";
import TabsLayout from "@/app/(tabs)/_layout";
import ProductsScreen from "@/app/(tabs)/products";
import ScanScreen from "@/app/(tabs)/scan";
import SettingsScreen from "@/app/(tabs)/settings";
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

/** A stand-in home screen that, like the real one, needs the user to be signed in. */
function Home() {
  const client = useApiClient();
  useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(client.GET("/api/v1/me")),
  });
  return <Text>Home screen</Text>;
}

/** A tab button's accessible name, for example "Home, tab, 1 of 4". */
const tabName = (label: string) => new RegExp(`^${label}, tab,`);

/**
 * Renders the app's routes, with the real root stack and screens and a stand-in home screen, and
 * returns a function for the current path.
 */
async function renderApp(): Promise<() => string> {
  const app = renderRouter({
    _layout: () => (
      <ApiProvider storage={storage}>
        <RootStack />
      </ApiProvider>
    ),
    "(tabs)/_layout": TabsLayout,
    "(tabs)/index": Home,
    "(tabs)/products": ProductsScreen,
    "(tabs)/scan": ScanScreen,
    "(tabs)/settings": SettingsScreen,
    connect: ConnectScreen,
    "update-required": UpdateRequiredScreen,
    setup: SetupScreen,
    login: LoginScreen,
    "product/[id]": ProductScreen,
  });
  await app;
  // The provider renders the routes once it has read the stored server.
  await screen.findByText(/Home screen|Enter the address|Sign in to/);
  // Not app itself: returning a promise from an async function unwraps it, which loses the helpers.
  return () => app.getPathname();
}

describe("RootStack", () => {
  it("opens the Connect screen while no server is stored", async () => {
    const pathname = await renderApp();

    expect(pathname()).toBe("/connect");
    expect(screen.getByLabelText("Server address")).toBeOnTheScreen();
  });

  it("opens the login screen when a server is stored", async () => {
    await storage.setServerUrl("https://stock.example.com");

    const pathname = await renderApp();

    expect(pathname()).toBe("/login");
    expect(
      screen.getByText("Sign in to https://stock.example.com."),
    ).toBeOnTheScreen();
  });

  it("opens the home screen after signing in", async () => {
    await storage.setServerUrl("https://stock.example.com");
    fetch.mockImplementation(async (input) =>
      new URL((input as Request).url).pathname === "/api/v1/auth/login"
        ? Response.json({
            token_type: "Bearer",
            access_token: "access-1",
            expires_in: 900,
            refresh_token: "refresh-1",
          })
        : Response.json(info),
    );
    const pathname = await renderApp();

    await fireEvent.changeText(screen.getByLabelText("Username"), "anna");
    await fireEvent.changeText(
      screen.getByLabelText("Password"),
      "correct horse",
    );
    await fireEvent.press(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Home screen")).toBeOnTheScreen();
    expect(pathname()).toBe("/");
  });

  it("keeps the user signed in after a restart", async () => {
    await storage.setServerUrl("https://stock.example.com");
    await storage.setTokens({
      accessToken: "access-1",
      refreshToken: "refresh-1",
    });

    const pathname = await renderApp();

    expect(screen.getByText("Home screen")).toBeOnTheScreen();
    expect(pathname()).toBe("/");
  });

  it("shows the tabs to signed-in users", async () => {
    await storage.setServerUrl("https://stock.example.com");
    await storage.setTokens({
      accessToken: "access-1",
      refreshToken: "refresh-1",
    });
    const pathname = await renderApp();

    for (const tab of ["Home", "Products", "Scan", "Settings"]) {
      expect(
        screen.getByRole("button", { name: tabName(tab) }),
      ).toBeOnTheScreen();
    }

    fetch.mockImplementation(async (input) =>
      new URL((input as Request).url).pathname === "/api/v1/products"
        ? Response.json({ items: [], next_cursor: null })
        : Response.json(info),
    );
    await fireEvent.press(
      screen.getByRole("button", { name: tabName("Products") }),
    );
    expect(
      await screen.findByText("There are no products yet."),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/products");

    await fireEvent.press(
      screen.getByRole("button", { name: tabName("Scan") }),
    );
    expect(
      await screen.findByText("Scanning barcodes will be here."),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/scan");
  });

  it("opens a product from the Products list", async () => {
    await storage.setServerUrl("https://stock.example.com");
    await storage.setTokens({
      accessToken: "access-1",
      refreshToken: "refresh-1",
    });
    const product = {
      id: "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37",
      sku: "WRK-CBT",
      name: "Cable ties",
      description: null,
      barcodes: ["4006381333931"],
      min_stock: 10,
      quantity: 2,
      archived_at: null,
      created_at: "2026-10-01T08:00:00Z",
      updated_at: "2026-10-01T08:00:00Z",
    };
    fetch.mockImplementation(async (input) => {
      switch (new URL((input as Request).url).pathname) {
        case "/api/v1/products":
          return Response.json({ items: [product], next_cursor: null });
        case `/api/v1/products/${product.id}`:
          return Response.json(product);
        default:
          return Response.json(info);
      }
    });
    const pathname = await renderApp();
    await fireEvent.press(
      screen.getByRole("button", { name: tabName("Products") }),
    );

    await fireEvent.press(
      await screen.findByRole("link", { name: /Cable ties/ }),
    );

    expect(await screen.findByText("4006381333931")).toBeOnTheScreen();
    expect(screen.getByText("Low on stock")).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${product.id}`);
  });

  it("shows no tabs to signed-out users", async () => {
    await storage.setServerUrl("https://stock.example.com");

    await renderApp();

    expect(
      screen.getByText("Sign in to https://stock.example.com."),
    ).toBeOnTheScreen();
    for (const tab of ["Home", "Products", "Scan", "Settings"]) {
      expect(
        screen.queryByRole("button", { name: tabName(tab) }),
      ).not.toBeOnTheScreen();
    }
  });

  it("returns to the login screen when the session has expired", async () => {
    await storage.setServerUrl("https://stock.example.com");
    await storage.setTokens({
      accessToken: "access-1",
      refreshToken: "refresh-1",
    });
    fetch.mockImplementation(async (input) =>
      new URL((input as Request).url).pathname === "/api/v1/info"
        ? Response.json(info)
        : Response.json(
            {
              error: {
                code: "invalid_refresh_token",
                message: "Refused.",
                details: null,
              },
            },
            { status: 401 },
          ),
    );

    const pathname = await renderApp();

    expect(
      await screen.findByText("Sign in to https://stock.example.com."),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/login");
    expect(await storage.getTokens()).toBeNull();
  });

  it("goes to the login screen after connecting", async () => {
    const pathname = await renderApp();

    await fireEvent.changeText(
      screen.getByLabelText("Server address"),
      "stock.example.com",
    );
    await fireEvent.press(screen.getByRole("button", { name: "Connect" }));

    expect(
      await screen.findByText("Sign in to https://stock.example.com."),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/login");
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

    expect(
      await screen.findByText("Sign in to https://stock.example.com."),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/login");
  });

  it("still offers to sign in while the server cannot be reached", async () => {
    await storage.setServerUrl("https://stock.example.com");
    fetch.mockRejectedValue(new TypeError("Network request failed"));

    const pathname = await renderApp();

    expect(pathname()).toBe("/login");
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

    expect(await screen.findByText("Home screen")).toBeOnTheScreen();
    expect(pathname()).toBe("/");
    // Signed in: the requests since carry the new access token.
    const lastRequest = fetch.mock.calls.at(-1)![0] as Request;
    expect(lastRequest.headers.get("Authorization")).toBe("Bearer access-1");
  });

  describe("from Settings", () => {
    let logoutBodies: unknown[];

    beforeEach(async () => {
      logoutBodies = [];
      await storage.setServerUrl("https://stock.example.com");
      await storage.setTokens({
        accessToken: "access-1",
        refreshToken: "refresh-1",
      });
      fetch.mockImplementation(async (input) => {
        const request = input as Request;
        switch (new URL(request.url).pathname) {
          case "/api/v1/auth/logout":
            logoutBodies.push(await request.json());
            return new Response(null, { status: 204 });
          case "/api/v1/me":
            return Response.json({
              id: "6f1c1a52-8a1e-4c63-9a54-1e1f4b8f2d10",
              username: "anna",
              display_name: "Anna Admin",
              role: "ADMIN",
            });
          default:
            return Response.json(info);
        }
      });
    });

    async function openSettings() {
      const pathname = await renderApp();
      await fireEvent.press(screen.getByText("Settings"));
      expect(
        await screen.findByText("Signed in as Anna Admin (anna)"),
      ).toBeOnTheScreen();
      expect(pathname()).toBe("/settings");
      return pathname;
    }

    it("signs out and returns to the login screen", async () => {
      const pathname = await openSettings();

      await fireEvent.press(screen.getByRole("button", { name: "Sign out" }));

      expect(
        await screen.findByText("Sign in to https://stock.example.com."),
      ).toBeOnTheScreen();
      expect(pathname()).toBe("/login");
      expect(logoutBodies).toEqual([{ refresh_token: "refresh-1" }]);
      expect(await storage.getTokens()).toBeNull();
    });

    it("changes the server after confirming, and returns to the Connect screen", async () => {
      const alert = jest.spyOn(Alert, "alert");
      const pathname = await openSettings();

      await fireEvent.press(
        screen.getByRole("button", { name: "Change server" }),
      );
      const [title, , buttons] = alert.mock.calls[0]!;
      expect(title).toBe("Change server?");
      await act(() =>
        buttons!.find((button) => button.style === "destructive")!.onPress!(),
      );

      expect(await screen.findByLabelText("Server address")).toBeOnTheScreen();
      expect(pathname()).toBe("/connect");
      expect(logoutBodies).toEqual([{ refresh_token: "refresh-1" }]);
      expect(await storage.getServerUrl()).toBeNull();
      expect(await storage.getTokens()).toBeNull();
      alert.mockRestore();
    });

    it("keeps the server when changing it is cancelled", async () => {
      const alert = jest.spyOn(Alert, "alert");
      const pathname = await openSettings();

      await fireEvent.press(
        screen.getByRole("button", { name: "Change server" }),
      );
      const [, , buttons] = alert.mock.calls[0]!;
      expect(
        buttons!.find((button) => button.style === "cancel")?.onPress,
      ).toBeUndefined();

      expect(pathname()).toBe("/settings");
      expect(await storage.getServerUrl()).toBe("https://stock.example.com");
      alert.mockRestore();
    });
  });

  it("offers a different server on the login screen", async () => {
    await storage.setServerUrl("https://stock.example.com");
    const pathname = await renderApp();

    await fireEvent.press(
      screen.getByRole("button", { name: "Use a different server" }),
    );

    expect(await screen.findByLabelText("Server address")).toBeOnTheScreen();
    expect(pathname()).toBe("/connect");
    expect(await storage.getServerUrl()).toBeNull();
  });
});
