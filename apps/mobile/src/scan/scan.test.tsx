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
import { router, Stack } from "expo-router";
import {
  act,
  fireEvent,
  renderRouter,
  screen,
} from "expo-router/testing-library";

import { ApiProvider } from "@/api/provider";
import AttachBarcodeScreen from "@/app/attach-barcode";
import NewProductScreen from "@/app/product/new";
import ProductScreen from "@/app/product/[id]";
import UnknownBarcodeScreen from "@/app/unknown-barcode";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

import { Scan } from "./scan";

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

const cableTies: Schema<"ProductResponse"> = {
  id: "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37",
  sku: "WRK-CBT-200",
  name: "Cable ties 200 mm, bag of 100",
  description: null,
  barcodes: ["4006381333931"],
  min_stock: 10,
  quantity: 2,
  archived_at: null,
  created_at: "2026-10-01T08:00:00Z",
  updated_at: "2026-10-01T08:00:00Z",
};

const errorResponse = (status: number, code: string) =>
  Response.json(
    { error: { code, message: "Error.", details: null } },
    { status },
  );

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let lookupResponse: () => Response | Promise<Response>;

beforeEach(() => {
  lookupResponse = () => Response.json(cableTies);
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    switch (new URL((input as Request).url).pathname) {
      case "/api/v1/products/lookup":
        return lookupResponse();
      case "/api/v1/stock/movements":
        return Response.json({ items: [], next_cursor: null });
      default:
        return errorResponse(404, "not_found");
    }
  });
});

afterEach(() => {
  fetch.mockRestore();
});

/** The requests sent to this path. */
const requestsTo = (pathname: string) =>
  fetch.mock.calls
    .map(([input]) => new URL((input as Request).url))
    .filter((url) => url.pathname === pathname);

/** Renders the Scan screen, with the product screen it opens, and returns a function for the current path. */
async function renderScan(): Promise<() => string> {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  const app = renderRouter({
    _layout: () => (
      <ApiProvider storage={storage}>
        <Stack />
      </ApiProvider>
    ),
    index: Scan,
    "product/[id]": ProductScreen,
    "product/new": NewProductScreen,
    "attach-barcode": AttachBarcodeScreen,
    "unknown-barcode": UnknownBarcodeScreen,
  });
  await app;
  // The provider renders the routes once it has read the stored server.
  await screen.findByTestId("barcode-scanner-camera");
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

describe("Scan", () => {
  it("opens the product with the scanned barcode", async () => {
    const pathname = await renderScan();

    await scan("4006381333931");

    expect(
      await screen.findByText("Cable ties 200 mm, bag of 100"),
    ).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${cableTies.id}`);
    const [lookup] = requestsTo("/api/v1/products/lookup");
    expect(lookup?.searchParams.get("barcode")).toBe("4006381333931");
  });

  it("says that it is looking the barcode up", async () => {
    let answer!: (response: Response) => void;
    lookupResponse = () => new Promise((resolve) => (answer = resolve));
    const pathname = await renderScan();

    await scan("4006381333931");

    expect(
      await screen.findByText("Looking up 4006381333931…"),
    ).toBeOnTheScreen();
    // Another barcode scanned meanwhile is ignored.
    await scan("0012345678905");
    expect(requestsTo("/api/v1/products/lookup")).toHaveLength(1);

    await act(() => answer(Response.json(cableTies)));
    expect(
      await screen.findByText("Cable ties 200 mm, bag of 100"),
    ).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${cableTies.id}`);
  });

  it("offers a sheet for a barcode that no product has", async () => {
    lookupResponse = () => errorResponse(404, "not_found");
    const pathname = await renderScan();

    await scan("0012345678905");

    expect(
      await screen.findByRole("heading", { name: "No product found" }),
    ).toBeOnTheScreen();
    expect(
      screen.getByText("No product has the barcode 0012345678905."),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/unknown-barcode");
  });

  it("opens the new product form with the unknown barcode", async () => {
    lookupResponse = () => errorResponse(404, "not_found");
    const pathname = await renderScan();
    await scan("0012345678905");

    await fireEvent.press(
      await screen.findByRole("button", { name: "Create product" }),
    );

    expect(
      (await screen.findByLabelText("Barcode (optional)")).props.value,
    ).toBe("0012345678905");
    expect(pathname()).toBe("/product/new");
    // The form replaced the sheet, so going back returns to the scanner.
    await act(() => router.back());
    expect(pathname()).toBe("/");
  });

  it("opens the attach screen with the unknown barcode", async () => {
    lookupResponse = () => errorResponse(404, "not_found");
    const pathname = await renderScan();
    await scan("0012345678905");

    await fireEvent.press(
      await screen.findByRole("button", {
        name: "Attach to existing product",
      }),
    );

    expect(
      await screen.findByText(
        "Pick the product that has the barcode 0012345678905.",
      ),
    ).toBeOnTheScreen();
    expect(pathname()).toBe("/attach-barcode");
    await act(() => router.back());
    expect(pathname()).toBe("/");
  });

  it("closes the sheet on Cancel", async () => {
    lookupResponse = () => errorResponse(404, "not_found");
    const pathname = await renderScan();
    await scan("0012345678905");

    await fireEvent.press(
      await screen.findByRole("button", { name: "Cancel" }),
    );

    expect(pathname()).toBe("/");
  });

  it("offers to try again when the lookup fails", async () => {
    lookupResponse = () => errorResponse(500, "internal_error");
    const pathname = await renderScan();

    await scan("4006381333931");
    expect(
      await screen.findByText(
        "The barcode 4006381333931 could not be looked up.",
      ),
    ).toBeOnTheScreen();

    lookupResponse = () => Response.json(cableTies);
    await fireEvent.press(screen.getByRole("button", { name: "Try again" }));

    expect(
      await screen.findByText("Cable ties 200 mm, bag of 100"),
    ).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${cableTies.id}`);
  });
});
