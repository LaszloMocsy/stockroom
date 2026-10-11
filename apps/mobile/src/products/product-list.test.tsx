import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { act, fireEvent, render, screen } from "@testing-library/react-native";

import { ApiProvider } from "@/api/provider";
import i18n from "@/i18n";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

import { ProductList } from "./product-list";

function product(
  name: string,
  sku: string,
  quantity: number,
  minStock: number | null = null,
) {
  return {
    id: crypto.randomUUID(),
    sku,
    name,
    description: null,
    barcodes: [],
    min_stock: minStock,
    quantity,
    archived_at: null,
    created_at: "2026-10-01T08:00:00Z",
    updated_at: "2026-10-01T08:00:00Z",
  };
}

const catalogue = [
  product("Ballpoint pens", "OFF-PEN", 120, 20),
  product("Cable ties", "WRK-CBT", 2, 10),
  product("Duct tape", "WRK-TAP", 0, 5),
  product("Zip bags", "KIT-ZIP", 40),
];

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

/**
 * Answers the product list like the API: products whose name or SKU contains `q`, two to a page, with
 * the index of the next one as the cursor.
 */
function serveCatalogue() {
  fetch.mockImplementation(async (input) => {
    const url = new URL((input as Request).url);
    if (url.pathname !== "/api/v1/products") {
      return Response.json(
        { error: { code: "not_found", message: "Not found.", details: null } },
        { status: 404 },
      );
    }
    const q = url.searchParams.get("q")?.toLowerCase() ?? "";
    const matches = catalogue.filter(
      ({ name, sku }) =>
        name.toLowerCase().includes(q) || sku.toLowerCase().includes(q),
    );
    const start = Number(url.searchParams.get("cursor") ?? 0);
    const end = start + 2;
    return Response.json({
      items: matches.slice(start, end),
      next_cursor: end < matches.length ? String(end) : null,
    });
  });
}

beforeEach(() => {
  fetch = jest.spyOn(globalThis, "fetch");
  serveCatalogue();
});

afterEach(() => {
  fetch.mockRestore();
});

async function renderList() {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  await render(
    <ApiProvider storage={storage}>
      <ProductList />
    </ApiProvider>,
  );
}

/** The query strings of the product list requests sent so far. */
const listRequests = () =>
  fetch.mock.calls
    .map(([input]) => new URL((input as Request).url))
    .filter((url) => url.pathname === "/api/v1/products")
    .map((url) => url.search);

const scrollToEnd = () =>
  fireEvent(screen.getByTestId("product-list"), "endReached");

describe("ProductList", () => {
  it("lists the first page of products with their quantities", async () => {
    await renderList();

    expect(await screen.findByText("Ballpoint pens")).toBeOnTheScreen();
    expect(screen.getByText("OFF-PEN")).toBeOnTheScreen();
    expect(screen.getByText("120 on hand · minimum 20")).toBeOnTheScreen();
    expect(screen.getByText("2 on hand · minimum 10")).toBeOnTheScreen();
    // Low stock in words, not only colour.
    expect(screen.getByText("Low on stock")).toBeOnTheScreen();
    expect(screen.queryByText("Duct tape")).not.toBeOnTheScreen();
    expect(listRequests()).toEqual([""]);
  });

  it("loads more as the user scrolls to the end", async () => {
    await renderList();
    await screen.findByText("Cable ties");

    await scrollToEnd();

    expect(await screen.findByText("Duct tape")).toBeOnTheScreen();
    expect(screen.getByText("Out of stock")).toBeOnTheScreen();
    expect(screen.getByText("Zip bags")).toBeOnTheScreen();
    // Without a minimum.
    expect(screen.getByText("40 on hand")).toBeOnTheScreen();
    expect(screen.getByText("Ballpoint pens")).toBeOnTheScreen();
    expect(listRequests()).toEqual(["", "?cursor=2"]);

    // The last page has no cursor, so there is nothing more to load.
    await scrollToEnd();
    expect(listRequests()).toHaveLength(2);
  });

  it("searches once the user stops typing", async () => {
    await renderList();
    await screen.findByText("Ballpoint pens");

    const search = screen.getByLabelText("Search");
    await fireEvent.changeText(search, "t");
    await fireEvent.changeText(search, "ta");
    await fireEvent.changeText(search, "tape");

    expect(await screen.findByText("Duct tape")).toBeOnTheScreen();
    expect(screen.queryByText("Ballpoint pens")).not.toBeOnTheScreen();
    expect(listRequests()).toEqual(["", "?q=tape"]);
  });

  it("lists every product again when the search is cleared", async () => {
    await renderList();
    await screen.findByText("Ballpoint pens");
    const search = screen.getByLabelText("Search");
    await fireEvent.changeText(search, "tape");
    await screen.findByText("Duct tape");

    await fireEvent.changeText(search, "  ");

    expect(await screen.findByText("Ballpoint pens")).toBeOnTheScreen();
  });

  it("says when no product matches", async () => {
    await renderList();
    await screen.findByText("Ballpoint pens");

    await fireEvent.changeText(screen.getByLabelText("Search"), "stapler");

    expect(
      await screen.findByText("No products match “stapler”."),
    ).toBeOnTheScreen();
  });

  it("says when there are no products", async () => {
    fetch.mockImplementation(async () =>
      Response.json({ items: [], next_cursor: null }),
    );

    await renderList();

    expect(
      await screen.findByText("There are no products yet."),
    ).toBeOnTheScreen();
  });

  it("shows an error when the list fails to load, and tries again", async () => {
    fetch.mockImplementation(async () =>
      Response.json(
        { error: { code: "bad_request", message: "Bad.", details: null } },
        { status: 400 },
      ),
    );
    await renderList();

    expect(
      await screen.findByText("The products could not be loaded."),
    ).toBeOnTheScreen();

    serveCatalogue();
    await fireEvent.press(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("Ballpoint pens")).toBeOnTheScreen();
  });

  it("keeps the loaded products when the next page fails, and tries again", async () => {
    await renderList();
    await screen.findByText("Cable ties");
    fetch.mockImplementation(async () =>
      Response.json(
        { error: { code: "bad_request", message: "Bad.", details: null } },
        { status: 400 },
      ),
    );

    await scrollToEnd();

    expect(
      await screen.findByText("More products could not be loaded."),
    ).toBeOnTheScreen();
    expect(screen.getByText("Ballpoint pens")).toBeOnTheScreen();

    serveCatalogue();
    await fireEvent.press(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("Duct tape")).toBeOnTheScreen();
  });

  it("takes its text from translation keys", async () => {
    // In i18next's "cimode", text is shown as its key.
    await i18n.changeLanguage("cimode");
    try {
      await renderList();

      expect(
        await screen.findAllByText("productRow.quantityAndMinimum"),
      ).toHaveLength(2);
      expect(screen.getByText("products.searchLabel")).toBeOnTheScreen();
    } finally {
      // Re-renders the component, so inside act.
      await act(() => i18n.changeLanguage("en"));
    }
  });
});
