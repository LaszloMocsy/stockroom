import { describe, expect, it, jest } from "@jest/globals";

import { checkServer, ConnectError } from "./check-server";

const info = {
  server_version: "0.1.0",
  api_version: "1.0",
  min_client_version: "0.0.0",
  setup_required: false,
};

const url = "https://stock.example.com";

/** A fetch that answers every request with `response`. */
function answering(response: () => Response | Promise<Response>) {
  return jest.fn(async (_request: Request) => response());
}

/** Expects the check to fail with a `ConnectError` with these properties. */
async function expectFailure(
  check: Promise<unknown>,
  expected: Partial<Pick<ConnectError, "reason" | "url" | "status">>,
) {
  const error = await check.catch((error: unknown) => error);
  expect(error).toBeInstanceOf(ConnectError);
  expect(error).toMatchObject(expected);
}

describe("checkServer", () => {
  it("returns the server's /info", async () => {
    const fetch = answering(() => Response.json(info));

    await expect(checkServer(url, { fetch })).resolves.toEqual(info);
    expect(fetch.mock.calls[0]![0].url).toBe(
      "https://stock.example.com/api/v1/info",
    );
  });

  it("ignores fields it does not know", async () => {
    const fetch = answering(() =>
      Response.json({ ...info, instance_name: "Warehouse" }),
    );

    await expect(checkServer(url, { fetch })).resolves.toEqual(info);
  });

  it("fails as unreachable when the request fails", async () => {
    const fetch = answering(() => {
      throw new TypeError("Network request failed");
    });

    await expectFailure(checkServer(url, { fetch }), {
      reason: "unreachable",
      url,
    });
  });

  it("fails as unreachable when the server does not answer in time", async () => {
    // Settles only when the request is aborted, as fetch does.
    const fetch = jest.fn(
      (request: Request) =>
        new Promise<Response>((_resolve, reject) => {
          request.signal.addEventListener("abort", () =>
            reject(request.signal.reason),
          );
        }),
    );

    await expectFailure(checkServer(url, { fetch, timeoutMs: 10 }), {
      reason: "unreachable",
    });
  });

  it.each([500, 502, 503])(
    "fails as a server error for HTTP %i",
    async (status) => {
      const fetch = answering(() => new Response("Bad gateway", { status }));

      await expectFailure(checkServer(url, { fetch }), {
        reason: "server_error",
        status,
      });
    },
  );

  it.each<[string, () => Response]>([
    ["a web page", () => new Response("<!doctype html><title>Hi</title>")],
    ["empty", () => new Response(null, { status: 204 })],
    ["other JSON", () => Response.json({ status: "ok" })],
    [
      "JSON with wrong types",
      () => Response.json({ ...info, setup_required: "no" }),
    ],
    ["a JSON array", () => Response.json([info])],
    [
      "a 404",
      () =>
        Response.json(
          {
            error: { code: "not_found", message: "Not found.", details: null },
          },
          { status: 404 },
        ),
    ],
    ["a 401", () => new Response("Unauthorized", { status: 401 })],
  ])("fails as not Stockroom when the answer is %s", async (_, response) => {
    const fetch = answering(response);

    await expectFailure(checkServer(url, { fetch }), {
      reason: "not_stockroom",
      url,
    });
  });
});
