import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import type { Schema } from "@stockroom/api-client";
import { Stack } from "expo-router";
import { fireEvent, renderRouter, screen } from "expo-router/testing-library";

import { ApiProvider } from "@/api/provider";
import AddStockScreen from "@/app/add-stock";
import ProductScreen from "@/app/product/[id]";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

// Numbered keys, so a test can tell which request a key was made for.
let mockKeys = 0;
jest.mock("expo-crypto", () => ({ randomUUID: () => `key-${++mockKeys}` }));

const productId = "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37";

/** The server's product, whose quantity the movements it records change. */
let product: Schema<"ProductResponse">;
/** Answers `POST /stock/movements` instead of recording the movement when set. */
let movementResponse: (() => Response | Promise<Response>) | null;
/** The movements sent, with their `Idempotency-Key`. */
let sent: { body: Schema<"CreateStockMovementRequest">; key: string | null }[];
let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

/** Answers like the API, from `product`. */
async function serve(input: RequestInfo | URL): Promise<Response> {
  const request = input as Request;
  const route = `${request.method} ${new URL(request.url).pathname}`;

  if (route === `GET /api/v1/products/${productId}`) {
    return Response.json(product);
  }
  if (route === "GET /api/v1/stock/movements") {
    return Response.json({ items: [], next_cursor: null });
  }
  if (route === "POST /api/v1/stock/movements") {
    const body = (await request.json()) as Schema<"CreateStockMovementRequest">;
    sent.push({ body, key: request.headers.get("Idempotency-Key") });
    if (movementResponse) {
      return movementResponse();
    }
    product = { ...product, quantity: product.quantity + body.quantity! };
    return Response.json(movement(body, product.quantity), { status: 201 });
  }
  return new Response(null, { status: 404 });
}

function movement(
  request: Schema<"CreateStockMovementRequest">,
  quantityAfter: number,
): Schema<"StockMovementResponse"> {
  return {
    id: "5c2e8f1a-3b7d-4a9e-8f61-0d4c2b9e7a13",
    product_id: productId,
    product_sku: product.sku,
    product_name: product.name,
    type: "receive",
    delta: request.quantity!,
    quantity_after: quantityAfter,
    reason: request.reason ?? "purchase",
    note: request.note ?? null,
    reference: null,
    voids_movement_id: null,
    actor_id: "9f1c2d3e-4b5a-4c6d-8e7f-0a1b2c3d4e5f",
    actor_name: "Ada",
    created_at: "2026-10-11T08:00:00Z",
  };
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
  movementResponse = null;
  sent = [];
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(serve);
});

afterEach(() => {
  fetch.mockRestore();
});

/** Opens the product, then its "Add stock" sheet, and returns a function for the current path. */
async function openSheet(): Promise<() => string> {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  const app = renderRouter(
    {
      _layout: () => (
        <ApiProvider storage={storage}>
          <Stack />
        </ApiProvider>
      ),
      "product/[id]": ProductScreen,
      "add-stock": AddStockScreen,
    },
    { initialUrl: `/product/${productId}` },
  );
  await app;
  await fireEvent.press(
    await screen.findByRole("button", { name: "Add stock" }),
  );
  // The sheet shows the product it adds to.
  await screen.findByText("WRK-CBT · 5 on hand");
  return () => app.getPathname();
}

const quantity = () => screen.getByLabelText("Units to add");

async function save() {
  await fireEvent.press(screen.getByRole("button", { name: "Save" }));
}

describe("AddStock", () => {
  it("adds the units and shows the product's new quantity", async () => {
    const pathname = await openSheet();
    expect(quantity().props.value).toBe("1");

    await fireEvent.press(screen.getByRole("button", { name: "One more" }));
    await fireEvent.press(screen.getByRole("button", { name: "One more" }));
    await fireEvent.press(screen.getByRole("button", { name: "One fewer" }));
    await fireEvent.press(screen.getByRole("radio", { name: "Return" }));
    await fireEvent.changeText(
      screen.getByLabelText("Note (optional)"),
      " Returned by the workshop ",
    );
    await save();

    expect(await screen.findByText("7")).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${productId}`);
    expect(sent).toEqual([
      {
        body: {
          product_id: productId,
          type: "receive",
          quantity: 2,
          reason: "return",
          note: "Returned by the workshop",
        },
        key: "key-1",
      },
    ]);
  });

  it("adds a typed quantity for a purchase without a note", async () => {
    await openSheet();
    expect(screen.getByRole("radio", { name: "Purchase" })).toBeChecked();

    await fireEvent.changeText(quantity(), "48");
    await save();

    expect(await screen.findByText("53")).toBeOnTheScreen();
    expect(sent.map(({ body }) => body)).toEqual([
      {
        product_id: productId,
        type: "receive",
        quantity: 48,
        reason: "purchase",
      },
    ]);
  });

  it.each([
    ["", "Enter how many."],
    ["0", "Enter at least 1."],
    ["2.5", "Enter a whole number, such as 12."],
    ["2000000", "Enter at most 1,000,000."],
  ])("does not save %j", async (text, message) => {
    await openSheet();

    await fireEvent.changeText(quantity(), text);
    await save();

    expect(screen.getByText(message)).toBeOnTheScreen();
    expect(sent).toEqual([]);
  });

  it("does not step below one", async () => {
    await openSheet();

    expect(screen.getByRole("button", { name: "One fewer" })).toBeDisabled();
  });

  it("saves again with the same key after the connection failed", async () => {
    movementResponse = () =>
      Promise.reject(new TypeError("Network request failed"));
    const pathname = await openSheet();

    await save();
    expect(
      await screen.findByText(
        "Cannot reach the server. Check your network connection and try again.",
      ),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/add-stock");

    movementResponse = null;
    await save();

    expect(await screen.findByText("6")).toBeOnTheScreen();
    expect(sent.map(({ key }) => key)).toEqual(["key-1", "key-1"]);
  });

  it("says when the product no longer exists", async () => {
    movementResponse = () => errorResponse(404, "not_found");
    await openSheet();

    await save();

    expect(
      await screen.findByText("This product no longer exists."),
    ).toBeOnTheScreen();
  });

  it("shows the server's refusal of a field next to it", async () => {
    movementResponse = () =>
      errorResponse(400, "validation_failed", {
        fields: { note: ["The note is too long."] },
      });
    await openSheet();

    await save();

    expect(await screen.findByText("The note is too long.")).toBeOnTheScreen();
  });
});
