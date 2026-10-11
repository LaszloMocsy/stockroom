import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import type { Schema } from "@stockroom/api-client";
import { fireEvent, render, screen } from "@testing-library/react-native";

import { AppProviders } from "@/navigation/app-providers";
import { ProductDetail } from "@/products/product-detail";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

// Numbered keys, so a test can tell which request a key was made for.
let mockKeys = 0;
jest.mock("expo-crypto", () => ({ randomUUID: () => `key-${++mockKeys}` }));

type Movement = Schema<"StockMovementResponse">;

const productId = "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37";

/** The server's product, whose quantity the movements it records change. */
let product: Schema<"ProductResponse">;
/** The movements the server recorded, by ID. */
let movements: Map<string, Movement>;
/** Answers a request instead of the server when set, by method and path, for example `POST /…/void`. */
let overrides: Record<string, () => Response | Promise<Response>>;
/** The requests sent that change stock, with their `Idempotency-Key`. */
let sent: { route: string; key: string | null }[];
/** The movements recorded by `Idempotency-Key`, which the server returns again for a retry. */
let byKey: Map<string, Movement>;
/** Routes, as in `overrides`, whose next answer the connection loses after the server recorded it. */
let losing: Set<string>;
let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

const voidRoute = (id: string) => `POST /api/v1/stock/movements/${id}/void`;

/**
 * Answers like the API: a request with a key that recorded a movement returns that movement again and
 * records nothing.
 */
async function serve(input: RequestInfo | URL): Promise<Response> {
  const request = input as Request;
  const route = `${request.method} ${new URL(request.url).pathname}`;
  const key = request.headers.get("Idempotency-Key");
  if (request.method === "POST") {
    sent.push({ route, key });
  }
  const retried = key === null ? undefined : byKey.get(key);
  if (retried) {
    return Response.json(retried, { status: 201 });
  }

  const response = await answer(request, route);
  if (key !== null && response.status === 201) {
    byKey.set(key, (await response.clone().json()) as Movement);
  }
  if (losing.delete(route) || losing.delete(routePattern(route))) {
    throw new TypeError("Network request failed");
  }
  return response;
}

/** Answers like the API, from `product` and `movements`, without regard to keys. */
async function answer(request: Request, route: string): Promise<Response> {
  const override = overrides[route] ?? overrides[routePattern(route)];
  if (override) {
    return override();
  }

  if (route === `GET /api/v1/products/${productId}`) {
    return Response.json(product);
  }
  if (route === "GET /api/v1/stock/movements") {
    return Response.json({ items: [], next_cursor: null });
  }
  if (route === "POST /api/v1/stock/movements") {
    const body = (await request.json()) as Schema<"CreateStockMovementRequest">;
    const delta = body.type === "issue" ? -body.quantity! : body.quantity!;
    return record(body.type!, delta, null);
  }
  const voided = /^POST \/api\/v1\/stock\/movements\/(.+)\/void$/.exec(route);
  if (voided) {
    const original = movements.get(voided[1]!)!;
    if (
      [...movements.values()].some((m) => m.voids_movement_id === original.id)
    ) {
      return errorResponse(409, "already_voided");
    }
    return record("void", -original.delta, original.id);
  }
  return new Response(null, { status: 404 });
}

/** The route with a movement ID replaced by `{id}`. */
const routePattern = (route: string) =>
  route.replace(/movements\/[^/]+\/void$/, "movements/{id}/void");

function record(
  type: Movement["type"],
  delta: number,
  voids: string | null,
): Response {
  if (product.quantity + delta < 0) {
    return errorResponse(409, "insufficient_stock", {
      requested: -delta,
      available: product.quantity,
    });
  }
  product = { ...product, quantity: product.quantity + delta };
  const movement: Movement = {
    id: `movement-${movements.size + 1}`,
    product_id: productId,
    product_sku: product.sku,
    product_name: product.name,
    type,
    delta,
    quantity_after: product.quantity,
    reason: "other",
    note: null,
    reference: null,
    voids_movement_id: voids,
    actor_id: "9f1c2d3e-4b5a-4c6d-8e7f-0a1b2c3d4e5f",
    actor_name: "Ada",
    created_at: "2026-10-11T08:00:00Z",
  };
  movements.set(movement.id, movement);
  return Response.json(movement, { status: 201 });
}

const errorResponse = (status: number, code: string, details: unknown = null) =>
  Response.json({ error: { code, message: "Refused.", details } }, { status });

beforeEach(() => {
  mockKeys = 0;
  product = {
    id: productId,
    sku: "WRK-CBT",
    name: "Cable ties",
    description: null,
    barcodes: [],
    min_stock: null,
    quantity: 5,
    archived_at: null,
    created_at: "2026-10-01T08:00:00Z",
    updated_at: "2026-10-01T08:00:00Z",
  };
  movements = new Map();
  overrides = {};
  sent = [];
  byKey = new Map();
  losing = new Set();
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(serve);
});

afterEach(() => {
  fetch.mockRestore();
});

async function renderProduct() {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  await render(
    <AppProviders storage={storage}>
      <ProductDetail id={productId} />
    </AppProviders>,
  );
  await screen.findByText(String(product.quantity));
}

const press = (name: string) =>
  fireEvent.press(screen.getByRole("button", { name }));

describe("quick stock buttons", () => {
  it("add one, and undo it within the undo window", async () => {
    await renderProduct();

    await press("Add one");

    expect(
      await screen.findByText("Added 1 to Cable ties. 6 on hand."),
    ).toBeOnTheScreen();
    expect(await screen.findByText("6")).toBeOnTheScreen();

    await press("Undo");

    expect(
      await screen.findByText("Undone. Cable ties has 5 on hand."),
    ).toBeOnTheScreen();
    expect(await screen.findByText("5")).toBeOnTheScreen();
    expect(product.quantity).toBe(5);
    expect(sent).toEqual([
      { route: "POST /api/v1/stock/movements", key: "key-1" },
      { route: voidRoute("movement-1"), key: "key-2" },
    ]);
  });

  it("remove one, and undo it", async () => {
    await renderProduct();

    await press("Remove one");
    expect(
      await screen.findByText("Removed 1 from Cable ties. 4 on hand."),
    ).toBeOnTheScreen();
    await press("Undo");

    expect(
      await screen.findByText("Undone. Cable ties has 5 on hand."),
    ).toBeOnTheScreen();
    expect(await screen.findByText("5")).toBeOnTheScreen();
  });

  it("adds one for each tap, and undoes the last", async () => {
    await renderProduct();

    await press("Add one");
    await screen.findByText("Added 1 to Cable ties. 6 on hand.");
    await press("Add one");
    await screen.findByText("Added 1 to Cable ties. 7 on hand.");
    await press("Undo");

    expect(
      await screen.findByText("Undone. Cable ties has 6 on hand."),
    ).toBeOnTheScreen();
    expect(sent.map(({ route }) => route)).toEqual([
      "POST /api/v1/stock/movements",
      "POST /api/v1/stock/movements",
      voidRoute("movement-2"),
    ]);
  });

  it("says when it is too late to undo", async () => {
    overrides["POST /api/v1/stock/movements/{id}/void"] = () =>
      errorResponse(403, "undo_window_expired");
    await renderProduct();

    await press("Add one");
    await screen.findByText("Added 1 to Cable ties. 6 on hand.");
    await press("Undo");

    expect(
      await screen.findByText(
        "It is too late to undo this. Ask an administrator to reverse it.",
      ),
    ).toBeOnTheScreen();
    expect(product.quantity).toBe(6);
  });

  it("says when the units added are no longer on hand to undo", async () => {
    await renderProduct();

    await press("Add one");
    await screen.findByText("Added 1 to Cable ties. 6 on hand.");
    // Someone else removed them all meanwhile.
    product = { ...product, quantity: 0 };
    await press("Undo");

    expect(
      await screen.findByText(
        "Cannot undo: the units added are no longer on hand.",
      ),
    ).toBeOnTheScreen();
  });

  it("says when there are none on hand to remove", async () => {
    product = { ...product, quantity: 0 };
    await renderProduct();

    await press("Remove one");

    expect(
      await screen.findByText("There are none on hand to remove."),
    ).toBeOnTheScreen();
    expect(screen.queryByRole("button", { name: "Undo" })).toBeNull();
  });

  it("tries a dropped request again with its key, so it counts once", async () => {
    overrides["POST /api/v1/stock/movements"] = () =>
      Promise.reject(new TypeError("Network request failed"));
    await renderProduct();

    await press("Add one");
    expect(
      await screen.findByText(
        "Could not add 1. Check your network connection.",
      ),
    ).toBeOnTheScreen();
    delete overrides["POST /api/v1/stock/movements"];
    await press("Try again");

    expect(
      await screen.findByText("Added 1 to Cable ties. 6 on hand."),
    ).toBeOnTheScreen();
    expect(sent.map(({ key }) => key)).toEqual(["key-1", "key-1"]);
  });

  it("adds one once when the server recorded it but its answer was lost", async () => {
    losing.add("POST /api/v1/stock/movements");
    await renderProduct();

    await press("Add one");
    expect(
      await screen.findByText(
        "Could not add 1. Check your network connection.",
      ),
    ).toBeOnTheScreen();
    expect(product.quantity).toBe(6);
    await press("Try again");

    expect(
      await screen.findByText("Added 1 to Cable ties. 6 on hand."),
    ).toBeOnTheScreen();
    expect(product.quantity).toBe(6);
    expect(sent.map(({ key }) => key)).toEqual(["key-1", "key-1"]);
  });

  it("undoes once when the server voided it but its answer was lost", async () => {
    await renderProduct();
    await press("Add one");
    await screen.findByText("Added 1 to Cable ties. 6 on hand.");
    losing.add("POST /api/v1/stock/movements/{id}/void");

    await press("Undo");
    expect(
      await screen.findByText("Could not undo. Check your network connection."),
    ).toBeOnTheScreen();
    expect(product.quantity).toBe(5);
    await press("Try again");

    // Not "already undone": the retry gets the void that the lost answer carried.
    expect(
      await screen.findByText("Undone. Cable ties has 5 on hand."),
    ).toBeOnTheScreen();
    expect(product.quantity).toBe(5);
    expect(sent).toEqual([
      { route: "POST /api/v1/stock/movements", key: "key-1" },
      { route: voidRoute("movement-1"), key: "key-2" },
      { route: voidRoute("movement-1"), key: "key-2" },
    ]);
  });
});
