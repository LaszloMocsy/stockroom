import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
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

import { Home } from "./home";

const summary = {
  total_products: 42,
  total_units: 12345,
  low_stock_count: 3,
  out_of_stock_count: 1,
  recent_movements: [],
};

function product(
  name: string,
  sku: string,
  quantity: number,
  minStock: number,
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

const lowStock = {
  items: [
    product("Cable ties", "SKU-0001", 2, 10),
    product("Duct tape", "SKU-0002", 0, 5),
  ],
  next_cursor: "next-page",
};

const notFound = () =>
  Response.json(
    { error: { code: "not_found", message: "Not found.", details: null } },
    { status: 404 },
  );

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

/** Answers the summary and the product list with these responses, or the defaults above. */
function serve(responses: {
  summary?: () => Response;
  products?: () => Response;
}) {
  fetch.mockImplementation(async (input) => {
    switch (new URL((input as Request).url).pathname) {
      case "/api/v1/stats/summary":
        return responses.summary?.() ?? Response.json(summary);
      case "/api/v1/products":
        return responses.products?.() ?? Response.json(lowStock);
      default:
        return notFound();
    }
  });
}

beforeEach(() => {
  fetch = jest.spyOn(globalThis, "fetch");
  serve({});
});

afterEach(() => {
  fetch.mockRestore();
});

async function renderHome() {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  await render(
    <ApiProvider storage={storage}>
      <Home />
    </ApiProvider>,
  );
}

/** The URLs of the requests sent to this path. */
const requestsTo = (path: string) =>
  fetch.mock.calls
    .map(([input]) => new URL((input as Request).url))
    .filter((url) => url.pathname === path);

describe("Home", () => {
  it("shows the summary figures from /stats/summary", async () => {
    // No low-stock products, whose badges would say "Out of stock" too.
    serve({
      products: () => Response.json({ items: [], next_cursor: null }),
    });
    await renderHome();

    expect(await screen.findByText("42")).toBeOnTheScreen();
    expect(screen.getByText("12,345")).toBeOnTheScreen();
    expect(screen.getByText("Units on hand")).toBeOnTheScreen();
    expect(screen.getByText("Out of stock")).toBeOnTheScreen();
    expect(requestsTo("/api/v1/stats/summary")).toHaveLength(1);
  });

  it("previews the products low on stock, saying how low in words", async () => {
    await renderHome();

    const cableTies = await screen.findByRole("link", { name: /Cable ties/ });
    expect(within(cableTies).getByText("SKU-0001")).toBeOnTheScreen();
    expect(
      within(cableTies).getByText("2 on hand · minimum 10"),
    ).toBeOnTheScreen();
    expect(within(cableTies).getByText("Low on stock")).toBeOnTheScreen();
    const ductTape = screen.getByRole("link", { name: /Duct tape/ });
    expect(
      within(ductTape).getByText("0 on hand · minimum 5"),
    ).toBeOnTheScreen();
    expect(within(ductTape).getByText("Out of stock")).toBeOnTheScreen();

    const [url] = requestsTo("/api/v1/products");
    expect(url!.searchParams.get("low_stock")).toBe("true");
    expect(url!.searchParams.get("limit")).toBe("5");
  });

  it("says when no product is low on stock", async () => {
    serve({
      products: () => Response.json({ items: [], next_cursor: null }),
    });

    await renderHome();

    expect(
      await screen.findByText("No products are low on stock."),
    ).toBeOnTheScreen();
  });

  it("shows an error for a section that fails to load, and tries again", async () => {
    serve({ summary: notFound });
    await renderHome();

    expect(
      await screen.findByText("The overview could not be loaded."),
    ).toBeOnTheScreen();
    // The other section still loads.
    expect(screen.getByText("Cable ties")).toBeOnTheScreen();

    serve({});
    await fireEvent.press(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("42")).toBeOnTheScreen();
    expect(
      screen.queryByText("The overview could not be loaded."),
    ).not.toBeOnTheScreen();
  });

  it("reloads both sections when pulled down", async () => {
    await renderHome();
    await screen.findByText("42");
    serve({
      summary: () => Response.json({ ...summary, total_products: 43 }),
      products: () =>
        Response.json({
          items: [product("Zip bags", "SKU-0003", 1, 4)],
          next_cursor: null,
        }),
    });

    // The test renderer leaves the refresh control out, so pull through its props.
    const { refreshControl } = screen.getByTestId("home").props;
    await act(() => refreshControl.props.onRefresh());

    expect(await screen.findByText("43")).toBeOnTheScreen();
    expect(screen.getByText("Zip bags")).toBeOnTheScreen();
    expect(screen.queryByText("Cable ties")).not.toBeOnTheScreen();
  });

  it("takes its text from translation keys", async () => {
    // In i18next's "cimode", text is shown as its key.
    await i18n.changeLanguage("cimode");
    try {
      await renderHome();

      expect(await screen.findByText("home.summaryHeading")).toBeOnTheScreen();
      expect(await screen.findByText("home.seeAllLowStock")).toBeOnTheScreen();
    } finally {
      // Re-renders the component, so inside act.
      await act(() => i18n.changeLanguage("en"));
    }
  });
});
