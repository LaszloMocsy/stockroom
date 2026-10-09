import { describe, expect, it, vi } from "vitest";
import { createApiClient, type ApiClientOptions } from "./index.js";

function mockFetch() {
  return vi.fn(
    async (_request: Request) =>
      new Response(JSON.stringify({ server_version: "0.1.0" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
  );
}

function setup(options: Partial<ApiClientOptions> = {}) {
  const fetch = mockFetch();
  const client = createApiClient({
    baseUrl: "https://stock.example.test",
    clientVersion: "1.2.3",
    clientPlatform: "ios",
    fetch,
    ...options,
  });

  /** The request the client sent, after the middleware ran. */
  const sent = () => {
    expect(fetch).toHaveBeenCalledOnce();
    return fetch.mock.calls[0]![0];
  };

  return { client, fetch, sent };
}

describe("createApiClient", () => {
  it("sends requests to the base URL", async () => {
    const { client, sent } = setup();

    const { data } = await client.GET("/api/v1/info");

    expect(sent().url).toBe("https://stock.example.test/api/v1/info");
    expect(data?.server_version).toBe("0.1.0");
  });

  it.each([
    "https://stock.example.test/",
    "https://stock.example.test//",
    "  https://stock.example.test  ",
  ])("normalises the base URL %j", async (baseUrl) => {
    const { client, sent } = setup({ baseUrl });

    await client.GET("/api/v1/info");

    expect(sent().url).toBe("https://stock.example.test/api/v1/info");
  });

  it("keeps a base URL path, for a server behind a path prefix", async () => {
    const { client, sent } = setup({
      baseUrl: "https://example.test/stockroom/",
    });

    await client.GET("/api/v1/info");

    expect(sent().url).toBe("https://example.test/stockroom/api/v1/info");
  });

  it("sends the client version and platform headers", async () => {
    const { client, sent } = setup();

    await client.GET("/api/v1/info");

    expect(sent().headers.get("X-Client-Version")).toBe("1.2.3");
    expect(sent().headers.get("X-Client-Platform")).toBe("ios");
  });

  it("injects the access token as a bearer token", async () => {
    const { client, sent } = setup({ getAccessToken: () => "access-token" });

    await client.GET("/api/v1/me");

    expect(sent().headers.get("Authorization")).toBe("Bearer access-token");
  });

  it("awaits an asynchronous token store", async () => {
    const { client, sent } = setup({
      getAccessToken: () => Promise.resolve("stored-token"),
    });

    await client.GET("/api/v1/me");

    expect(sent().headers.get("Authorization")).toBe("Bearer stored-token");
  });

  it("reads the token for every request", async () => {
    let token = "first";
    const { client, fetch } = setup({ getAccessToken: () => token });

    await client.GET("/api/v1/me");
    token = "second";
    await client.GET("/api/v1/me");

    const sent = fetch.mock.calls.map(([request]) =>
      request.headers.get("Authorization"),
    );
    expect(sent).toEqual(["Bearer first", "Bearer second"]);
  });

  it.each([null, undefined, ""])(
    "sends no Authorization header when the token is %j",
    async (token) => {
      const { client, sent } = setup({ getAccessToken: () => token });

      await client.GET("/api/v1/info");

      expect(sent().headers.has("Authorization")).toBe(false);
    },
  );

  it("keeps an Authorization header set by the caller", async () => {
    const { client, sent } = setup({ getAccessToken: () => "stored-token" });

    await client.GET("/api/v1/me", {
      headers: { Authorization: "Bearer explicit" },
    });

    expect(sent().headers.get("Authorization")).toBe("Bearer explicit");
  });

  it("keeps the request body and content type", async () => {
    const { client, sent } = setup({ getAccessToken: () => "access-token" });

    await client.POST("/api/v1/auth/login", {
      body: { username: "admin", password: "secret-password" },
    });

    expect(sent().method).toBe("POST");
    expect(sent().headers.get("Content-Type")).toBe("application/json");
    expect(await sent().json()).toEqual({
      username: "admin",
      password: "secret-password",
    });
  });
});
