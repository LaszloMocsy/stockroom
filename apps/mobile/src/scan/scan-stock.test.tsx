import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import type { Schema } from "@stockroom/api-client";
import type { BarcodeScanningResult } from "expo-camera";
import { Stack } from "expo-router";
import {
  act,
  fireEvent,
  renderRouter,
  screen,
} from "expo-router/testing-library";

import HomeScreen from "@/app/(tabs)/index";
import AddStockScreen from "@/app/add-stock";
import RemoveStockScreen from "@/app/remove-stock";
import ScanStockScreen from "@/app/scan-stock";
import UnknownBarcodeScreen from "@/app/unknown-barcode";
import { AppProviders } from "@/navigation/app-providers";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

// The camera is allowed, and is a plain view, so tests can fire `onBarcodeScanned`.
jest.mock("expo-camera", () => {
  const { View } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    ...jest.requireActual<object>("expo-camera"),
    CameraView: View,
    useCameraPermissions: () => [
      {
        status: "granted",
        granted: true,
        canAskAgain: true,
        expires: "never",
      },
      jest.fn(),
      jest.fn(),
    ],
  };
});

const barcode = "4006381333931";

/** The server's product, whose quantity the movements it records change. */
let product: Schema<"ProductResponse">;
/** The movements sent. */
let sent: Schema<"CreateStockMovementRequest">[];
let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

/** Answers like the API: Home's figures, the barcode lookup, the product, and recording movements. */
async function serve(input: RequestInfo | URL): Promise<Response> {
  const request = input as Request;
  const url = new URL(request.url);
  const route = `${request.method} ${url.pathname}`;

  switch (route) {
    case "GET /api/v1/stats/summary":
      return Response.json({
        total_products: 1,
        total_units: product.quantity,
        low_stock_count: 0,
        out_of_stock_count: 0,
        recent_movements: [],
      });
    case "GET /api/v1/products":
      return Response.json({ items: [], next_cursor: null });
    case "GET /api/v1/products/lookup":
      return url.searchParams.get("barcode") === barcode
        ? Response.json(product)
        : Response.json(
            { error: { code: "not_found", message: "No.", details: null } },
            { status: 404 },
          );
    case `GET /api/v1/products/${product.id}`:
      return Response.json(product);
    case "POST /api/v1/stock/movements": {
      const body =
        (await request.json()) as Schema<"CreateStockMovementRequest">;
      sent.push(body);
      const delta = body.type === "issue" ? -body.quantity! : body.quantity!;
      product = { ...product, quantity: product.quantity + delta };
      return Response.json(
        {
          id: "5c2e8f1a-3b7d-4a9e-8f61-0d4c2b9e7a13",
          product_id: product.id,
          product_sku: product.sku,
          product_name: product.name,
          type: body.type!,
          delta,
          quantity_after: product.quantity,
          reason: body.reason!,
          note: null,
          reference: null,
          voids_movement_id: null,
          actor_id: "9f1c2d3e-4b5a-4c6d-8e7f-0a1b2c3d4e5f",
          actor_name: "Ada",
          created_at: "2026-10-11T08:00:00Z",
        } satisfies Schema<"StockMovementResponse">,
        { status: 201 },
      );
    }
    default:
      return new Response(null, { status: 404 });
  }
}

beforeEach(() => {
  product = {
    id: "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37",
    sku: "WRK-CBT",
    name: "Cable ties",
    description: null,
    barcodes: [barcode],
    min_stock: null,
    quantity: 5,
    archived_at: null,
    created_at: "2026-10-01T08:00:00Z",
    updated_at: "2026-10-01T08:00:00Z",
  };
  sent = [];
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(serve);
});

afterEach(() => {
  fetch.mockRestore();
});

/** Opens Home, and returns a function for the current path. */
async function renderHome(): Promise<() => string> {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  const app = renderRouter({
    _layout: () => (
      <AppProviders storage={storage}>
        <Stack />
      </AppProviders>
    ),
    index: HomeScreen,
    "scan-stock": ScanStockScreen,
    "add-stock": AddStockScreen,
    "remove-stock": RemoveStockScreen,
    "unknown-barcode": UnknownBarcodeScreen,
  });
  await app;
  await screen.findByRole("button", { name: "Add stock" });
  return () => app.getPathname();
}

/** Has the camera read this barcode. */
async function scan(data: string) {
  const result: BarcodeScanningResult = {
    type: "ean13",
    data,
    cornerPoints: [],
    bounds: { origin: { x: 0, y: 0 }, size: { width: 0, height: 0 } },
  };
  await act(() =>
    screen.getByTestId("barcode-scanner-camera").props.onBarcodeScanned(result),
  );
}

describe("Home's stock shortcuts", () => {
  it("receive a delivery with two taps, a scan, and the quantity", async () => {
    const pathname = await renderHome();

    // Tap 1: Home's "Add stock" opens the scanner.
    await fireEvent.press(screen.getByRole("button", { name: "Add stock" }));
    expect(pathname()).toBe("/scan-stock");
    expect(
      screen.getByText("Scan a product to add stock to it."),
    ).toBeOnTheScreen();

    // The scan opens the product's sheet, which gives the quantity the keyboard once it is open.
    await scan(barcode);
    const quantity = await screen.findByLabelText("Units to add");
    expect(pathname()).toBe("/add-stock");

    // The quantity, then tap 2: the keyboard's Done saves.
    await fireEvent.changeText(quantity, "12");
    await fireEvent(quantity, "submitEditing");

    // Back at the scanner for the next product, with the delivery confirmed and Undo offered.
    expect(
      await screen.findByText("Added 12 to Cable ties. 17 on hand."),
    ).toBeOnTheScreen();
    expect(screen.getByRole("button", { name: "Undo" })).toBeOnTheScreen();
    expect(pathname()).toBe("/scan-stock");
    expect(sent).toEqual([
      {
        product_id: product.id,
        type: "receive",
        quantity: 12,
        reason: "purchase",
      },
    ]);
  });

  it("remove stock from a scanned product", async () => {
    const pathname = await renderHome();

    await fireEvent.press(screen.getByRole("button", { name: "Remove stock" }));
    expect(
      screen.getByText("Scan a product to remove stock from it."),
    ).toBeOnTheScreen();
    await scan(barcode);
    const quantity = await screen.findByLabelText("Units to remove");
    expect(pathname()).toBe("/remove-stock");

    await fireEvent.changeText(quantity, "2");
    await fireEvent(quantity, "submitEditing");

    expect(
      await screen.findByText("Removed 2 from Cable ties. 3 on hand."),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/scan-stock");
    expect(sent.map(({ type }) => type)).toEqual(["issue"]);
  });

  it("offers to create or attach a barcode that no product has", async () => {
    const pathname = await renderHome();

    await fireEvent.press(screen.getByRole("button", { name: "Add stock" }));
    await scan("0000000000000");

    expect(await screen.findByText("No product found")).toBeOnTheScreen();
    expect(pathname()).toBe("/unknown-barcode");
  });
});
