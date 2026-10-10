import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import type { Schema } from "@stockroom/api-client";
import { router, Stack } from "expo-router";
import { fireEvent, renderRouter, screen } from "expo-router/testing-library";

import { ApiProvider } from "@/api/provider";
import NewProductScreen from "@/app/product/new";
import ProductScreen from "@/app/product/[id]";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

/** The product the server creates from the request it gets. */
function created(
  request: Schema<"CreateProductRequest">,
): Schema<"ProductResponse"> {
  return {
    id: "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37",
    sku: request.sku ?? "SR-000123",
    name: request.name,
    description: null,
    barcodes: request.barcode ? [request.barcode] : [],
    min_stock: request.min_stock ?? null,
    quantity: request.initial_quantity ?? 0,
    archived_at: null,
    created_at: "2026-10-10T08:00:00Z",
    updated_at: "2026-10-10T08:00:00Z",
  };
}

/** Answers requests by method and path, for example `POST /api/v1/products`, and records the bodies sent. */
let routes: Record<string, (body: unknown) => Response>;
let sent: Record<string, unknown[]>;
let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

beforeEach(() => {
  routes = {
    "POST /api/v1/products": (body) =>
      Response.json(created(body as Schema<"CreateProductRequest">), {
        status: 201,
      }),
    "GET /api/v1/stock/movements": () =>
      Response.json({ items: [], next_cursor: null }),
  };
  sent = {};
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const request = input as Request;
    const route = `${request.method} ${new URL(request.url).pathname}`;
    const text = await request.text();
    const body: unknown = text ? JSON.parse(text) : undefined;
    (sent[route] ??= []).push(body);
    return routes[route]?.(body) ?? new Response(null, { status: 404 });
  });
});

afterEach(() => {
  fetch.mockRestore();
});

/** Answers a request with an API error. */
function apiError(status: number, code: string, details: unknown = null) {
  return () =>
    Response.json(
      { error: { code, message: "Refused.", details } },
      { status },
    );
}

/** Opens the form at `url`, with the product screen it leads to, and returns a function for the current path. */
async function renderForm(url = "/product/new"): Promise<() => string> {
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
      "product/new": NewProductScreen,
      "product/[id]": ProductScreen,
    },
    { initialUrl: url },
  );
  await app;
  // The provider renders the routes once it has read the stored server.
  await screen.findByLabelText("Name");
  return () => app.getPathname();
}

const field = (label: string) => screen.getByLabelText(label);

async function submit() {
  await fireEvent.press(screen.getByRole("button", { name: "Create product" }));
}

describe("CreateProductForm", () => {
  it("creates the product with the barcode passed to it and opens it", async () => {
    const pathname = await renderForm("/product/new?barcode=4006381333931");
    expect(field("Barcode (optional)").props.value).toBe("4006381333931");

    await fireEvent.changeText(field("Name"), " Cable ties ");
    await fireEvent.changeText(field("Minimum stock (optional)"), "10");
    await fireEvent.changeText(field("Units on hand (optional)"), "25");
    await submit();

    expect(await screen.findByText("Cable ties")).toBeOnTheScreen();
    expect(screen.getByText("4006381333931")).toBeOnTheScreen();
    expect(screen.getByText("SR-000123")).toBeOnTheScreen();
    expect(pathname()).toBe("/product/0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37");
    // The product replaced the form, so going back does not return to it.
    expect(router.canGoBack()).toBe(false);
    expect(sent["POST /api/v1/products"]).toEqual([
      {
        name: "Cable ties",
        min_stock: 10,
        barcode: "4006381333931",
        initial_quantity: 25,
      },
    ]);
  });

  it("creates a product with a name alone", async () => {
    const pathname = await renderForm();
    expect(field("Barcode (optional)").props.value).toBe("");

    await fireEvent.changeText(field("Name"), "Duct tape");
    await submit();

    expect(await screen.findByText("Duct tape")).toBeOnTheScreen();
    expect(pathname()).toBe("/product/0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37");
    expect(sent["POST /api/v1/products"]).toEqual([{ name: "Duct tape" }]);
  });

  it("checks the fields before sending them", async () => {
    await renderForm();

    await fireEvent.changeText(field("Minimum stock (optional)"), "2.5");
    await fireEvent.changeText(field("Units on hand (optional)"), "2000000");
    await submit();

    expect(screen.getByText("This is required.")).toBeOnTheScreen();
    expect(
      screen.getByText("Enter a whole number, such as 12."),
    ).toBeOnTheScreen();
    expect(screen.getByText("Enter at most 1,000,000.")).toBeOnTheScreen();
    expect(sent["POST /api/v1/products"]).toBeUndefined();
  });

  it("shows a SKU that another product has next to the SKU", async () => {
    routes["POST /api/v1/products"] = apiError(409, "sku_taken", {
      sku: "WRK-CBT",
      product_id: "7d0f3c1e-2b4a-4f6e-8c9d-1a2b3c4d5e6f",
    });
    await renderForm();

    await fireEvent.changeText(field("Name"), "Cable ties");
    await fireEvent.changeText(field("SKU (optional)"), "WRK-CBT");
    await submit();

    expect(
      await screen.findByText("Another product already has this SKU."),
    ).toBeOnTheScreen();
    expect(field("SKU (optional)").props["aria-invalid"]).toBe(true);
  });

  it("shows a barcode that another product has next to the barcode", async () => {
    routes["POST /api/v1/products"] = apiError(409, "barcode_taken", {
      barcode: "4006381333931",
      product_id: "7d0f3c1e-2b4a-4f6e-8c9d-1a2b3c4d5e6f",
    });
    await renderForm("/product/new?barcode=4006381333931");

    await fireEvent.changeText(field("Name"), "Cable ties");
    await submit();

    expect(
      await screen.findByText("Another product already has this barcode."),
    ).toBeOnTheScreen();
    expect(field("Barcode (optional)").props["aria-invalid"]).toBe(true);
  });

  it("shows the server's errors next to their fields", async () => {
    routes["POST /api/v1/products"] = apiError(400, "validation_failed", {
      fields: { name: ["The name cannot be blank."] },
    });
    await renderForm();

    await fireEvent.changeText(field("Name"), "Cable ties");
    await submit();

    expect(
      await screen.findByText("The name cannot be blank."),
    ).toBeOnTheScreen();
  });

  it("says so when the server fails", async () => {
    routes["POST /api/v1/products"] = apiError(500, "internal_error");
    await renderForm();

    await fireEvent.changeText(field("Name"), "Cable ties");
    await submit();

    expect(
      await screen.findByText(
        "The server could not create the product (HTTP 500). Try again.",
      ),
    ).toBeOnTheScreen();
  });

  it("says so when the server cannot be reached", async () => {
    routes["POST /api/v1/products"] = () => {
      throw new TypeError("Network request failed");
    };
    await renderForm();

    await fireEvent.changeText(field("Name"), "Cable ties");
    await submit();

    expect(
      await screen.findByText(
        "Cannot reach the server. Check your network connection and try again.",
      ),
    ).toBeOnTheScreen();
  });
});
