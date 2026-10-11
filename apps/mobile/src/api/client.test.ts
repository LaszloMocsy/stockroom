import { describe, expect, it, jest } from "@jest/globals";
import { ClientHeaders } from "@stockroom/api-client";

import { clientVersion, createClient } from "./client";

describe("createClient", () => {
  it("identifies the app and its platform to the server", async () => {
    const fetch = jest.fn(async (_request: Request) =>
      Response.json({
        server_version: "0.1.0",
        api_version: "1.0",
        min_client_version: "0.0.0",
        setup_required: false,
      }),
    );
    const client = createClient({
      baseUrl: "https://stock.example.test/",
      fetch,
    });

    const { data } = await client.GET("/api/v1/info");

    expect(data?.api_version).toBe("1.0");
    const request = fetch.mock.calls[0]![0];
    expect(request.url).toBe("https://stock.example.test/api/v1/info");
    expect(request.headers.get(ClientHeaders.version)).toBe(clientVersion);
    expect(request.headers.get(ClientHeaders.platform)).toBe("ios");
  });
});
