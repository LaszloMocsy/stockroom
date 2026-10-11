import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import type { Schema } from "@stockroom/api-client";
import {
  act,
  fireEvent,
  render,
  screen,
  within,
} from "@testing-library/react-native";

import { ApiProvider } from "@/api/provider";
import i18n from "@/i18n";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

import { LowStockList } from "./low-stock-list";

function product(
  name: string,
  quantity: number,
  minStock: number,
): Schema<"ProductResponse"> {
  return {
    id: crypto.randomUUID(),
    sku: name.toUpperCase().replaceAll(" ", "-"),
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

const lowStock = [
  product("Cable ties", 2, 10),
  product("Duct tape", 0, 5),
  product("Nitrile gloves", 3, 3),
];

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

/** Answers the product list with these products, two to a page, with the next one's index as the cursor. */
function serve(products: Schema<"ProductResponse">[]) {
  fetch.mockImplementation(async (input) => {
    const url = new URL((input as Request).url);
    const start = Number(url.searchParams.get("cursor") ?? 0);
    const end = start + 2;
    return Response.json({
      items: products.slice(start, end),
      next_cursor: end < products.length ? String(end) : null,
    });
  });
}

beforeEach(() => {
  fetch = jest.spyOn(globalThis, "fetch");
  serve(lowStock);
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
      <LowStockList />
    </ApiProvider>,
  );
}

/** The query strings of the requests sent so far. */
const requests = () =>
  fetch.mock.calls.map(([input]) => new URL((input as Request).url).search);

describe("LowStockList", () => {
  it("lists the products at or below their minimum, including those out of stock", async () => {
    await renderList();

    const cableTies = await screen.findByRole("link", { name: /Cable ties/ });
    expect(
      within(cableTies).getByText("2 on hand · minimum 10"),
    ).toBeOnTheScreen();
    expect(within(cableTies).getByText("Low on stock")).toBeOnTheScreen();
    const ductTape = screen.getByRole("link", { name: /Duct tape/ });
    expect(within(ductTape).getByText("Out of stock")).toBeOnTheScreen();
    expect(requests()).toEqual(["?low_stock=true"]);
  });

  it("loads more as the user scrolls to the end", async () => {
    await renderList();
    await screen.findByText("Duct tape");

    await fireEvent(screen.getByTestId("low-stock-list"), "endReached");

    const gloves = await screen.findByRole("link", { name: /Nitrile gloves/ });
    expect(within(gloves).getByText("Low on stock")).toBeOnTheScreen();
    expect(requests()).toEqual(["?low_stock=true", "?low_stock=true&cursor=2"]);
  });

  it("says when no product is low on stock", async () => {
    serve([]);

    await renderList();

    expect(
      await screen.findByText("No products are low on stock."),
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

    serve(lowStock);
    await fireEvent.press(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("Cable ties")).toBeOnTheScreen();
  });

  it("takes its text from translation keys", async () => {
    // In i18next's "cimode", text is shown as its key.
    await i18n.changeLanguage("cimode");
    try {
      serve([]);
      await renderList();

      expect(await screen.findByText("lowStock.none")).toBeOnTheScreen();
    } finally {
      // Re-renders the component, so inside act.
      await act(() => i18n.changeLanguage("en"));
    }
  });
});
