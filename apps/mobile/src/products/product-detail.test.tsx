import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import type { Schema } from "@stockroom/api-client";
import { act, fireEvent, render, screen } from "@testing-library/react-native";

import { ApiProvider } from "@/api/provider";
import i18n from "@/i18n";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

import { ProductDetail } from "./product-detail";

const id = "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37";

const cableTies: Schema<"ProductResponse"> = {
  id,
  sku: "WRK-CBT-200",
  name: "Cable ties 200 mm, bag of 100",
  description: "Black, UV resistant.",
  barcodes: ["4006381333931", "5012345678900"],
  min_stock: 10,
  quantity: 2,
  archived_at: null,
  created_at: "2026-10-01T08:00:00Z",
  updated_at: "2026-10-01T08:00:00Z",
};

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

let productResponse: () => Response;
let movementsResponse: (url: URL) => Response;

/** Answers `GET /products/{id}` with this response. */
function serve(response: () => Response) {
  productResponse = response;
}

const serveProduct = (changes: Partial<Schema<"ProductResponse">> = {}) =>
  serve(() => Response.json({ ...cableTies, ...changes }));

/** Answers the product's movements like the API: two to a page, with the next one's index as the cursor. */
function serveMovements(movements: Schema<"StockMovementResponse">[]) {
  movementsResponse = (url) => {
    const start = Number(url.searchParams.get("cursor") ?? 0);
    const end = start + 2;
    return Response.json({
      items: movements.slice(start, end),
      next_cursor: end < movements.length ? String(end) : null,
    });
  };
}

function movement(
  changes: Partial<Schema<"StockMovementResponse">>,
): Schema<"StockMovementResponse"> {
  return {
    id: crypto.randomUUID(),
    product_id: id,
    product_sku: cableTies.sku,
    product_name: cableTies.name,
    type: "receive",
    delta: 5,
    quantity_after: 5,
    reason: "purchase",
    note: null,
    reference: null,
    voids_movement_id: null,
    actor_id: "6f1c1a52-8a1e-4c63-9a54-1e1f4b8f2d10",
    actor_name: "Anna Admin",
    created_at: "2026-10-01T08:00:00Z",
    ...changes,
  };
}

const errorResponse = () =>
  Response.json(
    { error: { code: "bad_request", message: "Bad.", details: null } },
    { status: 400 },
  );

beforeEach(() => {
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = new URL((input as Request).url);
    switch (url.pathname) {
      case `/api/v1/products/${id}`:
        return productResponse();
      case "/api/v1/stock/movements":
        return movementsResponse(url);
      default:
        return Response.json({}, { status: 500 });
    }
  });
  serveProduct();
  serveMovements([]);
});

afterEach(() => {
  fetch.mockRestore();
});

async function renderDetail() {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  await render(
    <ApiProvider storage={storage}>
      <ProductDetail id={id} />
    </ApiProvider>,
  );
}

describe("ProductDetail", () => {
  it("shows the product's name, SKU, barcodes, quantity, and minimum", async () => {
    await renderDetail();

    expect(
      await screen.findByRole("heading", {
        name: "Cable ties 200 mm, bag of 100",
      }),
    ).toBeOnTheScreen();
    expect(screen.getByText("WRK-CBT-200")).toBeOnTheScreen();
    expect(screen.getByText("4006381333931")).toBeOnTheScreen();
    expect(screen.getByText("5012345678900")).toBeOnTheScreen();
    expect(screen.getByText("2")).toBeOnTheScreen();
    expect(screen.getByText("On hand")).toBeOnTheScreen();
    expect(screen.getByText("10")).toBeOnTheScreen();
    expect(screen.getByText("Black, UV resistant.")).toBeOnTheScreen();
    expect(screen.queryByText("This product is archived.")).toBeNull();
  });

  it("says in words when the product is low on stock", async () => {
    await renderDetail();

    expect(await screen.findByText("Low on stock")).toBeOnTheScreen();
  });

  it("says in words when the product is out of stock", async () => {
    serveProduct({ quantity: 0 });

    await renderDetail();

    expect(await screen.findByText("Out of stock")).toBeOnTheScreen();
    expect(screen.queryByText("Low on stock")).toBeNull();
  });

  it("shows no badge when there is enough stock", async () => {
    serveProduct({ quantity: 40 });

    await renderDetail();

    expect(await screen.findByText("40")).toBeOnTheScreen();
    expect(screen.queryByText("Low on stock")).toBeNull();
    expect(screen.queryByText("Out of stock")).toBeNull();
  });

  it("says when there is no minimum and no barcode", async () => {
    serveProduct({ min_stock: null, barcodes: [], description: null });

    await renderDetail();

    expect(await screen.findByText("Minimum stock")).toBeOnTheScreen();
    expect(screen.getAllByText("None")).toHaveLength(2);
    expect(screen.queryByText("Description")).toBeNull();
  });

  it("says when the product is archived", async () => {
    serveProduct({ archived_at: "2026-10-05T10:00:00Z" });

    await renderDetail();

    expect(
      await screen.findByText("This product is archived."),
    ).toBeOnTheScreen();
  });

  it("says when the product does not exist", async () => {
    serve(() =>
      Response.json(
        { error: { code: "not_found", message: "Not found.", details: null } },
        { status: 404 },
      ),
    );

    await renderDetail();

    expect(
      await screen.findByText("This product does not exist."),
    ).toBeOnTheScreen();
    expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
  });

  it("shows an error when the product fails to load, and tries again", async () => {
    serve(() =>
      Response.json(
        { error: { code: "bad_request", message: "Bad.", details: null } },
        { status: 400 },
      ),
    );
    await renderDetail();

    expect(
      await screen.findByText("The product could not be loaded."),
    ).toBeOnTheScreen();

    serveProduct();
    await fireEvent.press(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("WRK-CBT-200")).toBeOnTheScreen();
  });

  it("reloads when pulled down", async () => {
    await renderDetail();
    await screen.findByText("2");
    serveProduct({ quantity: 12 });

    // The test renderer leaves the refresh control out, so pull through its props.
    const { refreshControl } = screen.getByTestId("product").props;
    await act(() => refreshControl.props.onRefresh());

    expect(await screen.findByText("12")).toBeOnTheScreen();
  });

  describe("history", () => {
    const received = movement({
      type: "receive",
      delta: 12,
      quantity_after: 12,
      reason: "purchase",
      note: "Delivery 4711",
      actor_name: "Anna Admin",
    });
    const removed = movement({
      type: "issue",
      delta: -10,
      quantity_after: 2,
      reason: "damaged",
      actor_name: "Sam Staff",
    });
    const counted = movement({
      type: "adjust",
      delta: -1,
      quantity_after: 1,
      reason: "count",
    });
    const voidOfRemoved = movement({
      type: "void",
      delta: 10,
      quantity_after: 11,
      reason: "correction",
      voids_movement_id: removed.id,
    });

    it("lists who changed the stock, how, by how much, and why, newest first", async () => {
      serveMovements([removed, received]);

      await renderDetail();

      expect(await screen.findByText("History")).toBeOnTheScreen();
      expect(await screen.findByText("Removed")).toBeOnTheScreen();
      expect(screen.getByText("−10")).toBeOnTheScreen();
      expect(screen.getByText("Damaged · Sam Staff")).toBeOnTheScreen();
      expect(screen.getByText("Added")).toBeOnTheScreen();
      expect(screen.getByText("+12")).toBeOnTheScreen();
      expect(screen.getByText("Purchase · Anna Admin")).toBeOnTheScreen();
      expect(screen.getByText("Delivery 4711")).toBeOnTheScreen();

      const requests = fetch.mock.calls
        .map(([input]) => new URL((input as Request).url))
        .filter((url) => url.pathname === "/api/v1/stock/movements");
      expect(requests.map((url) => url.search)).toEqual([`?product=${id}`]);
    });

    it("marks a movement that a later one voided", async () => {
      serveMovements([voidOfRemoved, removed]);

      await renderDetail();

      expect(await screen.findByText("Removed (voided)")).toBeOnTheScreen();
      expect(screen.getByText("Void")).toBeOnTheScreen();
    });

    it("loads more as the user scrolls to the end", async () => {
      serveMovements([voidOfRemoved, counted, removed, received]);
      await renderDetail();
      await screen.findByText("Count set");
      expect(screen.queryByText("Added")).toBeNull();

      await fireEvent(screen.getByTestId("product"), "endReached");

      expect(await screen.findByText("Added")).toBeOnTheScreen();
      expect(screen.getByText("Removed (voided)")).toBeOnTheScreen();
    });

    it("says when there is no history", async () => {
      await renderDetail();

      expect(
        await screen.findByText("No stock movements yet."),
      ).toBeOnTheScreen();
    });

    it("shows an error when the history fails to load, and tries again", async () => {
      movementsResponse = errorResponse;
      await renderDetail();

      expect(
        await screen.findByText("The history could not be loaded."),
      ).toBeOnTheScreen();
      // The product itself still shows.
      expect(screen.getByText("WRK-CBT-200")).toBeOnTheScreen();

      serveMovements([received]);
      await fireEvent.press(screen.getByRole("button", { name: "Try again" }));

      expect(await screen.findByText("Added")).toBeOnTheScreen();
    });

    it("keeps the loaded history when the next page fails, and tries again", async () => {
      serveMovements([voidOfRemoved, counted, removed, received]);
      await renderDetail();
      await screen.findByText("Count set");
      const all = movementsResponse;
      movementsResponse = errorResponse;

      await fireEvent(screen.getByTestId("product"), "endReached");

      expect(
        await screen.findByText("More history could not be loaded."),
      ).toBeOnTheScreen();
      expect(screen.getByText("Count set")).toBeOnTheScreen();

      movementsResponse = all;
      await fireEvent.press(screen.getByRole("button", { name: "Try again" }));

      expect(await screen.findByText("Added")).toBeOnTheScreen();
    });
  });

  it("takes its text from translation keys", async () => {
    // In i18next's "cimode", text is shown as its key.
    await i18n.changeLanguage("cimode");
    try {
      await renderDetail();

      expect(await screen.findByText("product.onHand")).toBeOnTheScreen();
      expect(screen.getByText("stock.lowStock")).toBeOnTheScreen();
    } finally {
      // Re-renders the component, so inside act.
      await act(() => i18n.changeLanguage("en"));
    }
  });
});
