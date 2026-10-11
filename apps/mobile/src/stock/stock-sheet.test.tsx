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
import {
  fireEvent,
  renderRouter,
  screen,
  waitFor,
} from "expo-router/testing-library";

import { AppProviders } from "@/navigation/app-providers";
import AddStockScreen from "@/app/add-stock";
import ProductScreen from "@/app/product/[id]";
import RemoveStockScreen from "@/app/remove-stock";
import SetCountScreen from "@/app/set-count";
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

/** Answers like the API, from `product`, which never has fewer than none on hand. */
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
    if (body.type === "adjust") {
      if (
        body.expected_current !== undefined &&
        body.expected_current !== product.quantity
      ) {
        return errorResponse(409, "quantity_changed", {
          expected: body.expected_current,
          current: product.quantity,
        });
      }
      if (body.target_quantity === product.quantity) {
        return new Response(null, { status: 204 });
      }
    }
    const delta =
      body.type === "adjust"
        ? body.target_quantity! - product.quantity
        : body.type === "issue"
          ? -body.quantity!
          : body.quantity!;
    if (product.quantity + delta < 0) {
      return errorResponse(409, "insufficient_stock", {
        requested: body.quantity,
        available: product.quantity,
      });
    }
    product = { ...product, quantity: product.quantity + delta };
    return Response.json(movement(body, delta, product.quantity), {
      status: 201,
    });
  }
  return new Response(null, { status: 404 });
}

function movement(
  request: Schema<"CreateStockMovementRequest">,
  delta: number,
  quantityAfter: number,
): Schema<"StockMovementResponse"> {
  return {
    id: "5c2e8f1a-3b7d-4a9e-8f61-0d4c2b9e7a13",
    product_id: productId,
    product_sku: product.sku,
    product_name: product.name,
    type: request.type!,
    delta,
    quantity_after: quantityAfter,
    reason: request.reason!,
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

/** Opens the product, then the sheet that `button` opens, and returns a function for the current path. */
async function openSheet(button = "Add stock"): Promise<() => string> {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  const app = renderRouter(
    {
      _layout: () => (
        <AppProviders storage={storage}>
          <Stack />
        </AppProviders>
      ),
      "product/[id]": ProductScreen,
      "add-stock": AddStockScreen,
      "remove-stock": RemoveStockScreen,
      "set-count": SetCountScreen,
    },
    { initialUrl: `/product/${productId}` },
  );
  await app;
  await fireEvent.press(await screen.findByRole("button", { name: button }));
  // The sheet shows the product it changes.
  await screen.findByText(`WRK-CBT · ${product.quantity} on hand`);
  return () => app.getPathname();
}

const quantity = (label = "Units to add") => screen.getByLabelText(label);

async function save() {
  await fireEvent.press(screen.getByRole("button", { name: "Save" }));
}

describe("StockSheet adding stock", () => {
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
    // The toast confirms it, and offers to undo it.
    expect(
      screen.getByText("Added 2 to Cable ties. 7 on hand."),
    ).toBeOnTheScreen();
    expect(screen.getByRole("button", { name: "Undo" })).toBeOnTheScreen();
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

  it("saves from the quantity's Done key", async () => {
    const pathname = await openSheet();
    expect(quantity().props.returnKeyType).toBe("done");

    await fireEvent.changeText(quantity(), "12");
    await fireEvent(quantity(), "submitEditing");

    expect(await screen.findByText("17")).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${productId}`);
    expect(sent.map(({ body }) => body.quantity)).toEqual([12]);
  });

  it("checks the quantity when its Done key is pressed", async () => {
    await openSheet();

    await fireEvent.changeText(quantity(), "0");
    await fireEvent(quantity(), "submitEditing");

    expect(screen.getByText("Enter at least 1.")).toBeOnTheScreen();
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

describe("StockSheet removing stock", () => {
  const removeQuantity = () => quantity("Units to remove");

  it("removes the units and shows the product's new quantity", async () => {
    const pathname = await openSheet("Remove stock");
    expect(screen.getByRole("radio", { name: "Sale" })).toBeChecked();

    await fireEvent.changeText(removeQuantity(), "3");
    await fireEvent.press(screen.getByRole("radio", { name: "Damaged" }));
    await save();

    expect(await screen.findByText("2")).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${productId}`);
    expect(
      screen.getByText("Removed 3 from Cable ties. 2 on hand."),
    ).toBeOnTheScreen();
    expect(sent.map(({ body }) => body)).toEqual([
      {
        product_id: productId,
        type: "issue",
        quantity: 3,
        reason: "damaged",
      },
    ]);
  });

  it("says how many are on hand when removing more, and changes nothing", async () => {
    const pathname = await openSheet("Remove stock");
    // Someone else removed some since the sheet opened.
    product = { ...product, quantity: 2 };

    await fireEvent.changeText(removeQuantity(), "4");
    await save();

    expect(
      await screen.findByText("Only 2 are on hand. Remove at most 2."),
    ).toBeOnTheScreen();
    expect(screen.getByText("WRK-CBT · 2 on hand")).toBeOnTheScreen();
    expect(pathname()).toBe("/remove-stock");
    expect(product.quantity).toBe(2);

    await fireEvent.changeText(removeQuantity(), "2");
    expect(
      screen.queryByText("Only 2 are on hand. Remove at most 2."),
    ).not.toBeOnTheScreen();
    await save();

    expect(await screen.findByText("0")).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${productId}`);
  });

  it("says when there are none on hand", async () => {
    product = { ...product, quantity: 0 };
    await openSheet("Remove stock");

    await save();

    expect(
      await screen.findByText("There are none on hand to remove."),
    ).toBeOnTheScreen();
  });
});

describe("StockSheet setting a count", () => {
  const count = () => quantity("Counted quantity");

  it("shows the quantity it replaces and the difference, then sets it", async () => {
    const pathname = await openSheet("Set count");
    expect(count().props.value).toBe("");
    expect(screen.getByRole("radio", { name: "Count" })).toBeChecked();

    await fireEvent.changeText(count(), "3");
    expect(screen.getByText("Changes 5 on hand to 3 (−2).")).toBeOnTheScreen();
    await fireEvent.changeText(count(), "8");
    expect(screen.getByText("Changes 5 on hand to 8 (+3).")).toBeOnTheScreen();
    await save();

    expect(await screen.findByText("8")).toBeOnTheScreen();
    expect(screen.getByText("Set Cable ties to 8 on hand.")).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${productId}`);
    expect(sent.map(({ body }) => body)).toEqual([
      {
        product_id: productId,
        type: "adjust",
        target_quantity: 8,
        expected_current: 5,
        reason: "count",
      },
    ]);
  });

  it("accepts a count of none", async () => {
    await openSheet("Set count");

    await fireEvent.changeText(count(), "0");
    await save();

    expect(await screen.findByText("0")).toBeOnTheScreen();
  });

  it("asks for a count before saving", async () => {
    await openSheet("Set count");

    await save();

    expect(screen.getByText("Enter how many.")).toBeOnTheScreen();
    expect(sent).toEqual([]);
  });

  it("shows the new quantity when it changed meanwhile, and sets it once confirmed", async () => {
    const pathname = await openSheet("Set count");
    // Someone else added some since the sheet opened.
    product = { ...product, quantity: 7 };

    await fireEvent.changeText(count(), "6");
    await save();

    expect(
      await screen.findByText(
        "The quantity changed from 5 to 7 while you were counting. Check your count, then save again to confirm it.",
      ),
    ).toBeOnTheScreen();
    expect(screen.getByText("WRK-CBT · 7 on hand")).toBeOnTheScreen();
    expect(screen.getByText("Changes 7 on hand to 6 (−1).")).toBeOnTheScreen();
    expect(pathname()).toBe("/set-count");
    expect(product.quantity).toBe(7);

    await save();

    expect(await screen.findByText("6")).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${productId}`);
    expect(sent).toEqual([
      expect.objectContaining({
        body: expect.objectContaining({ expected_current: 5 }),
        key: "key-1",
      }),
      // A new request, as the first recorded nothing.
      expect.objectContaining({
        body: expect.objectContaining({ expected_current: 7 }),
        key: "key-2",
      }),
    ]);
  });

  it("closes without an error when the count matches the quantity", async () => {
    const pathname = await openSheet("Set count");

    await fireEvent.changeText(count(), "5");
    expect(
      screen.getByText("Matches the 5 on hand, so nothing will be recorded."),
    ).toBeOnTheScreen();
    await save();

    await waitFor(() => expect(pathname()).toBe(`/product/${productId}`));
    expect(screen.queryByRole("alert")).not.toBeOnTheScreen();
    // Nothing was recorded, so there is nothing to confirm or undo.
    expect(screen.queryByTestId("toast")).toBeNull();
    expect(screen.getByText("5")).toBeOnTheScreen();
    expect(sent).toHaveLength(1);
  });
});
