import { describe, expect, it } from "vitest";
import { ApiResponseError, createApiClient, unwrap } from "./index.js";

function clientAnswering(response: () => Response) {
  return createApiClient({
    baseUrl: "https://stock.example.test",
    clientVersion: "1.2.3",
    clientPlatform: "ios",
    fetch: async () => response(),
  });
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });

describe("unwrap", () => {
  it("returns the data of a successful response", async () => {
    const info = {
      server_version: "0.1.0",
      api_version: "1.0",
      min_client_version: "0.0.0",
      setup_required: false,
    };
    const client = clientAnswering(() => json(200, info));

    await expect(unwrap(client.GET("/api/v1/info"))).resolves.toEqual(info);
  });

  it("returns undefined for a response without a body", async () => {
    const client = clientAnswering(() => new Response(null, { status: 204 }));

    await expect(
      unwrap(
        client.POST("/api/v1/auth/logout", {
          body: { refresh_token: "refresh-1" },
        }),
      ),
    ).resolves.toBeUndefined();
  });

  it("throws the API's error code, message, and details", async () => {
    const client = clientAnswering(() =>
      json(409, {
        error: {
          code: "count_conflict",
          message: "The quantity changed.",
          details: { expected: 4, current: 6 },
        },
      }),
    );

    const error = await unwrap(client.GET("/api/v1/info")).catch(
      (e: unknown) => e,
    );

    expect(error).toBeInstanceOf(ApiResponseError);
    expect(error).toMatchObject({
      status: 409,
      code: "count_conflict",
      message: "The quantity changed.",
      details: { expected: 4, current: 6 },
    });
  });

  it("throws an error without a code for a body that is not the API's", async () => {
    const client = clientAnswering(
      () => new Response("<html>Bad Gateway</html>", { status: 502 }),
    );

    const error = await unwrap(client.GET("/api/v1/info")).catch(
      (e: unknown) => e,
    );

    expect(error).toBeInstanceOf(ApiResponseError);
    expect(error).toMatchObject({
      status: 502,
      code: null,
      message: "HTTP 502",
      details: null,
    });
  });

  it("passes network failures through", async () => {
    const client = createApiClient({
      baseUrl: "https://stock.example.test",
      clientVersion: "1.2.3",
      clientPlatform: "ios",
      fetch: () => Promise.reject(new TypeError("Network request failed")),
    });

    await expect(unwrap(client.GET("/api/v1/info"))).rejects.toThrow(
      new TypeError("Network request failed"),
    );
  });
});
