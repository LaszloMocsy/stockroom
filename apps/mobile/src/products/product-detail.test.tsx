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

/** Answers `GET /products/{id}` with this response. */
function serve(response: () => Response) {
  fetch.mockImplementation(async (input) =>
    new URL((input as Request).url).pathname === `/api/v1/products/${id}`
      ? response()
      : Response.json({}, { status: 500 }),
  );
}

const serveProduct = (changes: Partial<Schema<"ProductResponse">> = {}) =>
  serve(() => Response.json({ ...cableTies, ...changes }));

beforeEach(() => {
  fetch = jest.spyOn(globalThis, "fetch");
  serveProduct();
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
