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
import { fireEvent, render, screen } from "@testing-library/react-native";
import { Pressable, Text } from "react-native";

import { MemoryStore } from "@/storage/memory-store";
import {
  createAppStorage,
  StorageKeys,
  type AppStorage,
} from "@/storage/storage";

import {
  ApiProvider,
  useApiClient,
  useChangeServer,
  useServerUrl,
  useSignedIn,
  useSignOut,
} from "./provider";

const user = {
  id: "6f1c1a52-8a1e-4c63-9a54-1e1f4b8f2d10",
  username: "anna",
  display_name: "Anna Admin",
  role: "ADMIN",
};

const storedTokens = { accessToken: "access-1", refreshToken: "refresh-1" };

const refreshedTokens = {
  token_type: "Bearer",
  access_token: "access-2",
  expires_in: 900,
  refresh_token: "refresh-2",
};

const invalidToken = (code: string) =>
  Response.json(
    { error: { code, message: "Refused.", details: null } },
    { status: 401 },
  );

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let store: MemoryStore;
let storage: AppStorage;
/** The access tokens that `/me` accepts, and the refresh tokens that `/auth/refresh` accepts. */
let validAccessTokens: string[];
let validRefreshTokens: string[];
let refreshBodies: unknown[];
let logoutBodies: unknown[];
let logoutReachable: boolean;

beforeEach(async () => {
  validAccessTokens = ["access-1"];
  validRefreshTokens = ["refresh-1"];
  refreshBodies = [];
  logoutBodies = [];
  logoutReachable = true;
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const request = input as Request;
    switch (new URL(request.url).pathname) {
      case "/api/v1/me": {
        const token = request.headers
          .get("Authorization")
          ?.slice("Bearer ".length);
        return token && validAccessTokens.includes(token)
          ? Response.json(user)
          : invalidToken("unauthorized");
      }
      case "/api/v1/auth/refresh": {
        const body = (await request.json()) as { refresh_token: string };
        refreshBodies.push(body);
        return validRefreshTokens.includes(body.refresh_token)
          ? Response.json(refreshedTokens)
          : invalidToken("invalid_refresh_token");
      }
      case "/api/v1/auth/logout":
        if (!logoutReachable) {
          throw new TypeError("Network request failed");
        }
        logoutBodies.push(await request.json());
        return new Response(null, { status: 204 });
      default:
        return new Response(null, { status: 404 });
    }
  });
  store = new MemoryStore();
  storage = createAppStorage(store);
  await storage.setServerUrl("https://stock.example.com");
});

afterEach(() => {
  fetch.mockRestore();
});

/**
 * Shows the server, whether the user is signed in, and, when asked, who the server says the user is,
 * with buttons for the provider's actions.
 */
function Session({ fetchMe }: { fetchMe: boolean }) {
  const signedIn = useSignedIn();
  const serverUrl = useServerUrl();
  const signOut = useSignOut();
  const changeServer = useChangeServer();
  return (
    <>
      <Text testID="signed-in">{String(signedIn)}</Text>
      <Text testID="server">{serverUrl ?? "none"}</Text>
      {fetchMe && serverUrl && <Me />}
      <Pressable role="button" onPress={() => void signOut()}>
        <Text>Sign out</Text>
      </Pressable>
      <Pressable role="button" onPress={() => void changeServer()}>
        <Text>Change server</Text>
      </Pressable>
    </>
  );
}

function Me() {
  const client = useApiClient();
  const me = useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(client.GET("/api/v1/me")),
  });
  return (
    <Text testID="me">
      {me.data?.display_name ?? (me.error ? "refused" : "loading")}
    </Text>
  );
}

async function renderSession({ fetchMe = false } = {}) {
  await render(
    <ApiProvider storage={storage}>
      <Session fetchMe={fetchMe} />
    </ApiProvider>,
  );
  await screen.findByTestId("signed-in");
}

describe("ApiProvider session", () => {
  it("starts signed in with stored tokens and sends them", async () => {
    await storage.setTokens(storedTokens);

    await renderSession({ fetchMe: true });

    expect(screen.getByTestId("signed-in")).toHaveTextContent("true");
    expect(await screen.findByText("Anna Admin")).toBeOnTheScreen();
    expect(refreshBodies).toEqual([]);
  });

  it("starts signed out without stored tokens", async () => {
    await renderSession();

    expect(screen.getByTestId("signed-in")).toHaveTextContent("false");
  });

  it("ignores tokens stored without a server", async () => {
    await storage.clear();
    await storage.setTokens(storedTokens);

    await renderSession();

    expect(screen.getByTestId("signed-in")).toHaveTextContent("false");
  });

  it("starts signed out when the tokens cannot be read", async () => {
    store.items.set(StorageKeys.tokens, "not JSON");

    await renderSession();

    expect(screen.getByTestId("signed-in")).toHaveTextContent("false");
  });

  it("refreshes an expired access token without signing out", async () => {
    await storage.setTokens(storedTokens);
    validAccessTokens = ["access-2"];

    await renderSession({ fetchMe: true });

    expect(await screen.findByText("Anna Admin")).toBeOnTheScreen();
    expect(refreshBodies).toEqual([{ refresh_token: "refresh-1" }]);
    expect(await storage.getTokens()).toEqual({
      accessToken: "access-2",
      refreshToken: "refresh-2",
    });
    expect(screen.getByTestId("signed-in")).toHaveTextContent("true");
  });

  it("signs out when the session has expired", async () => {
    await storage.setTokens(storedTokens);
    validAccessTokens = [];
    validRefreshTokens = [];

    await renderSession({ fetchMe: true });

    expect(await screen.findByText("refused")).toBeOnTheScreen();
    expect(screen.getByTestId("signed-in")).toHaveTextContent("false");
    expect(await storage.getTokens()).toBeNull();
    expect(await storage.getServerUrl()).toBe("https://stock.example.com");
  });

  it("signs out: revokes the session and forgets the tokens", async () => {
    await storage.setTokens(storedTokens);
    await renderSession({ fetchMe: true });
    await screen.findByText("Anna Admin");

    await fireEvent.press(screen.getByRole("button", { name: "Sign out" }));

    expect(await screen.findByText("false")).toBeOnTheScreen();
    expect(logoutBodies).toEqual([{ refresh_token: "refresh-1" }]);
    expect(await storage.getTokens()).toBeNull();
    expect(screen.getByTestId("server")).toHaveTextContent(
      "https://stock.example.com",
    );
    // A new cache: the signed-in user's data is gone.
    expect(screen.queryByText("Anna Admin")).not.toBeOnTheScreen();
  });

  it("signs out even when the server cannot be reached", async () => {
    await storage.setTokens(storedTokens);
    logoutReachable = false;
    await renderSession();

    await fireEvent.press(screen.getByRole("button", { name: "Sign out" }));

    expect(await screen.findByText("false")).toBeOnTheScreen();
    expect(await storage.getTokens()).toBeNull();
  });

  it("changes the server: signs out and forgets the server", async () => {
    await storage.setTokens(storedTokens);
    await renderSession();

    await fireEvent.press(
      screen.getByRole("button", { name: "Change server" }),
    );

    expect(await screen.findByText("none")).toBeOnTheScreen();
    expect(screen.getByTestId("signed-in")).toHaveTextContent("false");
    expect(logoutBodies).toEqual([{ refresh_token: "refresh-1" }]);
    expect(await storage.getServerUrl()).toBeNull();
    expect(await storage.getTokens()).toBeNull();
  });

  it("changes the server without a session to revoke", async () => {
    await renderSession();

    await fireEvent.press(
      screen.getByRole("button", { name: "Change server" }),
    );

    expect(await screen.findByText("none")).toBeOnTheScreen();
    expect(logoutBodies).toEqual([]);
  });
});
