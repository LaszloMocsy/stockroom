import { describe, expect, it, vi } from "vitest";
import {
  createApiClient,
  type ApiClientOptions,
  type TokenResponse,
} from "./index.js";

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });

const unauthorised = () =>
  json(401, { error: { code: "unauthorized", message: "Log in." } });

/**
 * Stands in for the API: accepts only access tokens it issued, and rotates refresh tokens like the real
 * server, so reusing a refresh token fails.
 */
class FakeServer {
  private issued = 0;
  private readonly validAccessTokens = new Set<string>();
  private currentRefreshToken = "refresh-0";
  private held: (() => void)[] = [];
  private holdCount = 0;

  /** Every request the server received, with its Authorization header and body. */
  readonly received: {
    path: string;
    authorization: string | null;
    body: string;
  }[] = [];
  /** Requests to the refresh endpoint. */
  readonly refreshRequests: Request[] = [];
  /** Replaces the refresh endpoint's answer, for failure cases. */
  refreshResponse: (() => Response) | undefined;
  /** Rejects every access token, even freshly issued ones. */
  rejectAllAccessTokens = false;
  /** A 401 for this path waits until the promise settles. */
  readonly gates = new Map<string, Promise<void>>();

  readonly fetch = vi.fn(async (request: Request) => {
    const path = new URL(request.url).pathname;
    const authorization = request.headers.get("Authorization");
    this.received.push({ path, authorization, body: await request.text() });

    if (path === "/api/v1/auth/refresh") {
      this.refreshRequests.push(request);
      return (
        this.refreshResponse?.() ?? this.refresh(this.received.at(-1)!.body)
      );
    }

    const token = authorization?.replace(/^Bearer /, "");
    if (
      path.startsWith("/api/v1/auth/") ||
      this.rejectAllAccessTokens ||
      !token ||
      !this.validAccessTokens.has(token)
    ) {
      await this.gates.get(path);
      await this.holdRejection();
      return unauthorised();
    }

    return json(200, { ok: true });
  });

  /** Holds 401 responses until `count` of them are waiting, so the requests fail together. */
  rejectTogether(count: number) {
    this.holdCount = count;
  }

  private async holdRejection() {
    if (this.holdCount === 0) {
      return;
    }
    await new Promise<void>((release) => {
      this.held.push(release);
      if (this.held.length === this.holdCount) {
        this.held.forEach((r) => r());
        this.held = [];
        this.holdCount = 0;
      }
    });
  }

  private refresh(body: string): Response {
    const { refresh_token } = JSON.parse(body) as { refresh_token: string };
    if (refresh_token !== this.currentRefreshToken) {
      return json(401, {
        error: { code: "invalid_refresh_token", message: "Log in again." },
      });
    }

    this.issued += 1;
    const tokens: TokenResponse = {
      token_type: "Bearer",
      access_token: `access-${this.issued}`,
      expires_in: 900,
      refresh_token: `refresh-${this.issued}`,
    };
    this.validAccessTokens.add(tokens.access_token);
    this.currentRefreshToken = tokens.refresh_token;
    return json(200, tokens);
  }
}

/** The app's token storage, starting with an access token the server no longer accepts. */
function tokenStore(refreshToken: string | null = "refresh-0") {
  const store = {
    accessToken: "expired" as string | null,
    refreshToken,
    refreshed: [] as TokenResponse[],
    sessionExpired: 0,
  };

  const options = {
    getAccessToken: () => store.accessToken,
    tokenRefresh: {
      getRefreshToken: () => store.refreshToken,
      onTokensRefreshed: async (tokens: TokenResponse) => {
        await Promise.resolve(); // storage is asynchronous on devices
        store.accessToken = tokens.access_token;
        store.refreshToken = tokens.refresh_token;
        store.refreshed.push(tokens);
      },
      onSessionExpired: () => {
        store.sessionExpired += 1;
        store.accessToken = null;
        store.refreshToken = null;
      },
    },
  } satisfies Partial<ApiClientOptions>;

  return { store, options };
}

function setup(options: Partial<ApiClientOptions> = {}) {
  const server = new FakeServer();
  const tokens = tokenStore();
  const client = createApiClient({
    baseUrl: "https://stock.example.test",
    clientVersion: "1.2.3",
    clientPlatform: "android",
    fetch: server.fetch,
    ...tokens.options,
    ...options,
  });
  return { client, server, store: tokens.store };
}

describe("token refresh", () => {
  it("refreshes an expired access token and retries the request", async () => {
    const { client, server, store } = setup();

    const { data, response } = await client.GET("/api/v1/me");

    expect(response.status).toBe(200);
    expect(data).toEqual({ ok: true });
    expect(server.refreshRequests).toHaveLength(1);
    expect(store.refreshed.map((t) => t.access_token)).toEqual(["access-1"]);
    expect(store.refreshToken).toBe("refresh-1");
    expect(server.received.map((r) => [r.path, r.authorization])).toEqual([
      ["/api/v1/me", "Bearer expired"],
      ["/api/v1/auth/refresh", null],
      ["/api/v1/me", "Bearer access-1"],
    ]);
  });

  it("sends the refresh token with the client headers and no access token", async () => {
    const { client, server } = setup();

    await client.GET("/api/v1/me");

    const refresh = server.refreshRequests[0]!;
    expect(refresh.method).toBe("POST");
    expect(refresh.headers.get("Authorization")).toBeNull();
    expect(refresh.headers.get("X-Client-Version")).toBe("1.2.3");
    expect(refresh.headers.get("X-Client-Platform")).toBe("android");
    expect(JSON.parse(server.received[1]!.body)).toEqual({
      refresh_token: "refresh-0",
    });
  });

  it("shares one refresh between requests that fail together", async () => {
    const { client, server, store } = setup();
    server.rejectTogether(3);

    const responses = await Promise.all([
      client.GET("/api/v1/me"),
      client.GET("/api/v1/stats/summary"),
      client.GET("/api/v1/products"),
    ]);

    expect(responses.map((r) => r.response.status)).toEqual([200, 200, 200]);
    expect(server.refreshRequests).toHaveLength(1);
    expect(store.sessionExpired).toBe(0);
    const retries = server.received.filter(
      (r) => r.authorization === "Bearer access-1",
    );
    expect(retries.map((r) => r.path).sort()).toEqual([
      "/api/v1/me",
      "/api/v1/products",
      "/api/v1/stats/summary",
    ]);
  });

  it("uses the token from a refresh that finished after the request was sent", async () => {
    const { client, server, store } = setup();
    let release!: () => void;
    server.gates.set(
      "/api/v1/stats/summary",
      new Promise((resolve) => (release = resolve)),
    );

    // Sent with the expired token; its 401 arrives only after the other request has refreshed.
    const late = client.GET("/api/v1/stats/summary");
    await client.GET("/api/v1/me");
    release();

    expect((await late).response.status).toBe(200);
    expect(server.refreshRequests).toHaveLength(1);
    expect(store.sessionExpired).toBe(0);
  });

  it("sends the request body again on retry", async () => {
    const { client, server } = setup();
    const body = {
      product_id: "0b6f1c1e-5f1a-4c4b-9e57-0f8a1f4f2a10",
      type: "issue" as const,
      quantity: 3,
      note: "Order #1042",
    };

    const { response } = await client.POST("/api/v1/stock/movements", {
      body,
      headers: { "Idempotency-Key": "7c1b" },
    });

    expect(response.status).toBe(200);
    const sent = server.received.filter(
      (r) => r.path === "/api/v1/stock/movements",
    );
    expect(sent).toHaveLength(2);
    expect(sent.map((r) => JSON.parse(r.body))).toEqual([body, body]);
    expect(server.fetch.mock.calls[2]![0].headers.get("Idempotency-Key")).toBe(
      "7c1b",
    );
  });

  it("ends the session once when the refresh token is rejected", async () => {
    const tokens = tokenStore("refresh-stolen");
    const { client, server } = setup(tokens.options);
    server.rejectTogether(2);

    const [me, summary] = await Promise.all([
      client.GET("/api/v1/me"),
      client.GET("/api/v1/stats/summary"),
    ]);

    expect(me.response.status).toBe(401);
    expect(summary.response.status).toBe(401);
    expect(me.error).toEqual({
      error: { code: "unauthorized", message: "Log in." },
    });
    expect(server.refreshRequests).toHaveLength(1);
    expect(tokens.store.sessionExpired).toBe(1);
  });

  it("ends the session when no refresh token is stored", async () => {
    const tokens = tokenStore(null);
    const { client, server } = setup(tokens.options);

    const { response } = await client.GET("/api/v1/me");

    expect(response.status).toBe(401);
    expect(server.refreshRequests).toHaveLength(0);
    expect(tokens.store.sessionExpired).toBe(1);
  });

  it.each([429, 500, 503])(
    "keeps the tokens when the refresh answers %i",
    async (status) => {
      const { client, server, store } = setup();
      server.refreshResponse = () =>
        json(status, { error: { code: "busy", message: "Try later." } });

      const { response } = await client.GET("/api/v1/me");

      expect(response.status).toBe(401);
      expect(store.sessionExpired).toBe(0);
      expect(store.refreshToken).toBe("refresh-0");
    },
  );

  it("fails the request when the refresh cannot reach the server, and refreshes again next time", async () => {
    const { client, server, store } = setup();
    server.refreshResponse = () => {
      throw new TypeError("Network request failed");
    };

    await expect(client.GET("/api/v1/me")).rejects.toThrow(
      "Network request failed",
    );
    expect(store.sessionExpired).toBe(0);

    server.refreshResponse = undefined;
    const { response } = await client.GET("/api/v1/me");

    expect(response.status).toBe(200);
    expect(server.refreshRequests).toHaveLength(2);
  });

  it("returns a second 401 without refreshing again", async () => {
    const { client, server } = setup();
    server.rejectAllAccessTokens = true;

    const { response } = await client.GET("/api/v1/me");

    expect(response.status).toBe(401);
    expect(server.refreshRequests).toHaveLength(1);
    expect(server.received.map((r) => r.path)).toEqual([
      "/api/v1/me",
      "/api/v1/auth/refresh",
      "/api/v1/me",
    ]);
  });

  it("does not refresh after a 401 from the auth endpoints", async () => {
    const { client, server } = setup();

    const { response } = await client.POST("/api/v1/auth/login", {
      body: { username: "admin", password: "wrong-password" },
    });

    expect(response.status).toBe(401);
    expect(server.refreshRequests).toHaveLength(0);
  });

  it("does not refresh a request sent without an access token", async () => {
    const { client, server } = setup({ getAccessToken: () => null });

    const { response } = await client.GET("/api/v1/me");

    expect(response.status).toBe(401);
    expect(server.refreshRequests).toHaveLength(0);
  });

  it("does not refresh a request whose caller set Authorization", async () => {
    const { client, server } = setup();

    const { response } = await client.GET("/api/v1/me", {
      headers: { Authorization: "Bearer explicit" },
    });

    expect(response.status).toBe(401);
    expect(server.refreshRequests).toHaveLength(0);
  });

  it("returns the 401 as is without tokenRefresh", async () => {
    const server = new FakeServer();
    const client = createApiClient({
      baseUrl: "https://stock.example.test",
      clientVersion: "1.2.3",
      clientPlatform: "android",
      fetch: server.fetch,
      getAccessToken: () => "expired",
    });

    const { response } = await client.GET("/api/v1/me");

    expect(response.status).toBe(401);
    expect(server.refreshRequests).toHaveLength(0);
  });
});
