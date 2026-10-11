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
  act,
  fireEvent,
  renderRouter,
  screen,
  waitFor,
} from "expo-router/testing-library";
import { Alert } from "react-native";

import EditProductScreen from "@/app/product/edit";
import ProductScreen from "@/app/product/[id]";
import { AppProviders } from "@/navigation/app-providers";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

const id = "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37";
/** A QR payload, whose `/` must survive as part of one path segment. */
const qrBarcode = "https://example.com/p/42";

/** The server's product, which the requests it gets change. */
let product: Schema<"ProductResponse">;
/** Answers a request by method and path instead of the server when set. */
let overrides: Record<string, () => Response>;
/** The changes and removals sent. */
let patches: Schema<"UpdateProductRequest">[];
let removals: string[];
let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let alert: jest.SpiedFunction<typeof Alert.alert>;

const errorResponse = (status: number, code: string, details: unknown = null) =>
  Response.json({ error: { code, message: "Refused.", details } }, { status });

/** Answers like the API, from `product`. */
async function serve(input: RequestInfo | URL): Promise<Response> {
  const request = input as Request;
  const url = new URL(request.url);
  const route = `${request.method} ${url.pathname}`;
  const override = overrides[route];
  if (override) {
    return override();
  }

  if (route === `GET /api/v1/products/${id}`) {
    return Response.json(product);
  }
  if (route === "GET /api/v1/stock/movements") {
    return Response.json({ items: [], next_cursor: null });
  }
  if (route === `PATCH /api/v1/products/${id}`) {
    const changes = (await request.json()) as Schema<"UpdateProductRequest">;
    patches.push(changes);
    product = {
      ...product,
      ...(changes.name !== undefined && { name: changes.name! }),
      ...(changes.description !== undefined && {
        description: changes.description,
      }),
      ...(changes.min_stock !== undefined && { min_stock: changes.min_stock }),
    };
    return Response.json(product);
  }
  const prefix = `DELETE /api/v1/products/${id}/barcodes/`;
  if (route.startsWith(prefix)) {
    // The path as sent, so a test can check how the barcode was encoded.
    const encoded = url.pathname.slice(prefix.length - "DELETE ".length);
    const barcode = decodeURIComponent(encoded);
    removals.push(encoded);
    if (!product.barcodes.includes(barcode)) {
      return errorResponse(404, "not_found");
    }
    product = {
      ...product,
      barcodes: product.barcodes.filter((b) => b !== barcode),
    };
    return Response.json(product);
  }
  return new Response(null, { status: 404 });
}

beforeEach(() => {
  product = {
    id,
    sku: "WRK-CBT-200",
    name: "Cable ties",
    description: "Black.",
    barcodes: ["4006381333931", qrBarcode],
    min_stock: 10,
    quantity: 2,
    archived_at: null,
    created_at: "2026-10-01T08:00:00Z",
    updated_at: "2026-10-01T08:00:00Z",
  };
  overrides = {};
  patches = [];
  removals = [];
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(serve);
  alert = jest.spyOn(Alert, "alert").mockImplementation(() => {});
});

afterEach(() => {
  fetch.mockRestore();
  alert.mockRestore();
});

/** Opens the product, then its edit screen from the header, and returns a function for the current path. */
async function openEdit(): Promise<() => string> {
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
      "product/edit": EditProductScreen,
    },
    { initialUrl: `/product/${id}` },
  );
  await app;
  await fireEvent.press(
    await screen.findByRole("button", { name: "Edit product" }),
  );
  await screen.findByLabelText("Name");
  return () => app.getPathname();
}

const field = (label: string) => screen.getByLabelText(label);

async function save() {
  await fireEvent.press(screen.getByRole("button", { name: "Save changes" }));
}

/** Answers the last confirmation with the button `text`. */
async function answerAlert(text: string) {
  const [, , buttons] = alert.mock.calls.at(-1)!;
  await act(() => buttons!.find((b) => b.text === text)?.onPress?.());
}

describe("EditProduct", () => {
  it("saves the changes, and the product shows them", async () => {
    const pathname = await openEdit();
    expect(field("Name").props.value).toBe("Cable ties");
    expect(field("Description (optional)").props.value).toBe("Black.");
    expect(field("Minimum stock (optional)").props.value).toBe("10");
    expect(screen.getByText("WRK-CBT-200")).toBeOnTheScreen();

    await fireEvent.changeText(field("Name"), " Cable ties, black ");
    await fireEvent.changeText(field("Minimum stock (optional)"), "");
    await save();

    expect(
      await screen.findByRole("heading", { name: "Cable ties, black" }),
    ).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${id}`);
    // The minimum is gone, and the description stays.
    expect(screen.getByText("None", { exact: true })).toBeOnTheScreen();
    expect(screen.getByText("Black.")).toBeOnTheScreen();
    expect(screen.getByText("Saved Cable ties, black.")).toBeOnTheScreen();
    expect(patches).toEqual([{ name: "Cable ties, black", min_stock: null }]);
  });

  it("returns without a request when nothing changed", async () => {
    const pathname = await openEdit();

    await save();

    await waitFor(() => expect(pathname()).toBe(`/product/${id}`));
    expect(patches).toEqual([]);
  });

  it("checks the fields before saving", async () => {
    await openEdit();

    await fireEvent.changeText(field("Name"), "  ");
    await fireEvent.changeText(field("Minimum stock (optional)"), "2.5");
    await save();

    expect(screen.getByText("This is required.")).toBeOnTheScreen();
    expect(
      screen.getByText("Enter a whole number, such as 12."),
    ).toBeOnTheScreen();
    expect(patches).toEqual([]);
  });

  it("shows the server's refusal of a field next to it", async () => {
    overrides[`PATCH /api/v1/products/${id}`] = () =>
      errorResponse(400, "validation_failed", {
        fields: { description: ["The description is too long."] },
      });
    await openEdit();

    await fireEvent.changeText(field("Description (optional)"), "Longer.");
    await save();

    expect(
      await screen.findByText("The description is too long."),
    ).toBeOnTheScreen();
  });

  it("removes a barcode once confirmed, and the product no longer has it", async () => {
    await openEdit();

    await fireEvent.press(
      screen.getByRole("button", { name: `Remove barcode ${qrBarcode}` }),
    );
    expect(alert.mock.calls.at(-1)?.[1]).toBe(
      `Remove ${qrBarcode} from Cable ties? Scanning it will no longer find this product.`,
    );
    await answerAlert("Remove");

    await waitFor(() =>
      expect(
        screen.queryByRole("button", { name: `Remove barcode ${qrBarcode}` }),
      ).toBeNull(),
    );
    expect(removals).toEqual([encodeURIComponent(qrBarcode)]);
    expect(product.barcodes).toEqual(["4006381333931"]);

    // Going back, the product shows its barcodes without it.
    await fireEvent.press(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByText("4006381333931")).toBeOnTheScreen();
    expect(screen.queryByText(qrBarcode)).toBeNull();
  });

  it("keeps a barcode when the removal is cancelled", async () => {
    await openEdit();

    await fireEvent.press(
      screen.getByRole("button", { name: "Remove barcode 4006381333931" }),
    );
    await answerAlert("Cancel");

    expect(removals).toEqual([]);
    expect(
      screen.getByRole("button", { name: "Remove barcode 4006381333931" }),
    ).toBeOnTheScreen();
  });

  it("says when the barcode was already removed", async () => {
    await openEdit();
    product = { ...product, barcodes: [qrBarcode] };

    await fireEvent.press(
      screen.getByRole("button", { name: "Remove barcode 4006381333931" }),
    );
    await answerAlert("Remove");

    expect(
      await screen.findByText("This barcode was already removed."),
    ).toBeOnTheScreen();
  });
});
