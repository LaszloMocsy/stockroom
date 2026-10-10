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
  waitFor,
} from "expo-router/testing-library";
import { Alert } from "react-native";

import { ApiProvider } from "@/api/provider";
import AttachBarcodeScreen from "@/app/attach-barcode";
import ProductScreen from "@/app/product/[id]";
import { Scan } from "@/scan/scan";
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

function product(id: string, name: string, sku: string) {
  return {
    id,
    sku,
    name,
    description: null,
    barcodes: [] as string[],
    min_stock: null,
    quantity: 5,
    archived_at: null,
    created_at: "2026-10-01T08:00:00Z",
    updated_at: "2026-10-01T08:00:00Z",
  } satisfies Schema<"ProductResponse">;
}

const errorResponse = (status: number, code: string, details: unknown = null) =>
  Response.json({ error: { code, message: "Refused.", details } }, { status });

/** The server's products, changed by the requests it gets. */
let products: Schema<"ProductResponse">[];
/** Answers `POST /products/{id}/barcodes` instead of the server's products when set. */
let attachResponse: (() => Response) | null;
let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

/** Answers like the API, from `products`: attaching a barcode makes the lookup find the product. */
async function serve(input: RequestInfo | URL): Promise<Response> {
  const request = input as Request;
  const url = new URL(request.url);
  const find = (id: string | undefined) => products.find((p) => p.id === id);

  if (url.pathname === "/api/v1/products") {
    const q = url.searchParams.get("q")?.toLowerCase() ?? "";
    return Response.json({
      items: products.filter((p) => p.name.toLowerCase().includes(q)),
      next_cursor: null,
    });
  }
  if (url.pathname === "/api/v1/products/lookup") {
    const found = products.find((p) =>
      p.barcodes.includes(url.searchParams.get("barcode") ?? ""),
    );
    return found ? Response.json(found) : errorResponse(404, "not_found");
  }
  if (url.pathname === "/api/v1/stock/movements") {
    return Response.json({ items: [], next_cursor: null });
  }
  const attachTo = /^\/api\/v1\/products\/([^/]+)\/barcodes$/.exec(
    url.pathname,
  )?.[1];
  if (request.method === "POST" && attachTo) {
    if (attachResponse) {
      return attachResponse();
    }
    const target = find(attachTo);
    if (!target) {
      return errorResponse(404, "not_found");
    }
    const body = (await request.json()) as Schema<"AddBarcodeRequest">;
    target.barcodes = [...target.barcodes, body.barcode];
    return Response.json(target, { status: 201 });
  }
  const found = find(/^\/api\/v1\/products\/([^/]+)$/.exec(url.pathname)?.[1]);
  return found ? Response.json(found) : errorResponse(404, "not_found");
}

let alert: jest.SpiedFunction<typeof Alert.alert>;

beforeEach(() => {
  products = [
    product("0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37", "Cable ties", "WRK-CBT"),
    product("7d0f3c1e-2b4a-4f6e-8c9d-1a2b3c4d5e6f", "Duct tape", "WRK-TAP"),
  ];
  attachResponse = null;
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(serve);
  alert = jest.spyOn(Alert, "alert").mockImplementation(() => {});
});

afterEach(() => {
  fetch.mockRestore();
  alert.mockRestore();
});

const ductTape = () => products[1]!;

/** The requests that attached a barcode. */
const attachRequests = () =>
  fetch.mock.calls.filter(
    ([input]) =>
      (input as Request).method === "POST" &&
      new URL((input as Request).url).pathname.endsWith("/barcodes"),
  );

/** Opens the screen at `url`, with the Scan screen and the product screen, and returns a function for the current path. */
async function renderAttach(
  url = `/attach-barcode?barcode=${barcode}`,
): Promise<() => string> {
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
      index: Scan,
      "attach-barcode": AttachBarcodeScreen,
      "product/[id]": ProductScreen,
    },
    { initialUrl: url },
  );
  await app;
  return () => app.getPathname();
}

/** Picks the product named `name` from the list, and answers the confirmation with the button `button`. */
async function pick(name: string, button: "Attach" | "Cancel") {
  await fireEvent.press(
    await screen.findByRole("button", { name: new RegExp(name) }),
  );
  const [title, message, buttons] = alert.mock.calls.at(-1)!;
  expect(title).toBe("Attach barcode?");
  expect(message).toBe(
    `Attach ${barcode} to Duct tape (WRK-TAP)? Scanning it will then open this product.`,
  );
  await act(() => buttons!.find((b) => b.text === button)?.onPress?.());
}

describe("AttachBarcode", () => {
  it("attaches the barcode to the picked product, which the next scan then opens", async () => {
    const pathname = await renderAttach();
    expect(
      await screen.findByText(
        `Pick the product that has the barcode ${barcode}.`,
      ),
    ).toBeOnTheScreen();

    await fireEvent.changeText(screen.getByLabelText("Search"), "duct");
    // Only the matching product is left once the search has run.
    await waitFor(() =>
      expect(screen.queryByRole("button", { name: /Cable ties/ })).toBeNull(),
    );
    await pick("Duct tape", "Attach");

    expect(await screen.findByText(barcode)).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${ductTape().id}`);
    // The product replaced this screen, so going back does not return to it.
    expect(router.canGoBack()).toBe(false);

    await act(() => router.navigate("/"));
    expect(pathname()).toBe("/");
    const scanned: BarcodeScanningResult = {
      type: "ean13",
      data: barcode,
      cornerPoints: [],
      bounds: { origin: { x: 0, y: 0 }, size: { width: 0, height: 0 } },
    };
    await act(() =>
      screen
        .getByTestId("barcode-scanner-camera")
        .props.onBarcodeScanned(scanned),
    );

    await waitFor(() => expect(pathname()).toBe(`/product/${ductTape().id}`));
    const lookups = fetch.mock.calls
      .map(([input]) => new URL((input as Request).url))
      .filter((url) => url.pathname === "/api/v1/products/lookup");
    expect(lookups.map((url) => url.searchParams.get("barcode"))).toEqual([
      barcode,
    ]);
  });

  it("attaches nothing when the user cancels", async () => {
    const pathname = await renderAttach();

    await pick("Duct tape", "Cancel");

    expect(attachRequests()).toHaveLength(0);
    expect(pathname()).toBe("/attach-barcode");
  });

  it("offers to open the product that already has the barcode", async () => {
    attachResponse = () =>
      errorResponse(409, "barcode_taken", {
        barcode,
        product_id: products[0]!.id,
      });
    const pathname = await renderAttach();

    await pick("Duct tape", "Attach");

    expect(
      await screen.findByText(
        `Another product already has the barcode ${barcode}.`,
      ),
    ).toBeOnTheScreen();
    await fireEvent.press(
      screen.getByRole("button", { name: "Open that product" }),
    );
    expect(await screen.findByText("WRK-CBT")).toBeOnTheScreen();
    expect(pathname()).toBe(`/product/${products[0]!.id}`);
  });

  it("says so when the product no longer exists", async () => {
    attachResponse = () => errorResponse(404, "not_found");
    await renderAttach();

    await pick("Duct tape", "Attach");

    expect(
      await screen.findByText(
        "Duct tape no longer exists. Pick another product.",
      ),
    ).toBeOnTheScreen();
  });

  it("says so when the server fails", async () => {
    attachResponse = () => errorResponse(500, "internal_error");
    await renderAttach();

    await pick("Duct tape", "Attach");

    expect(
      await screen.findByText(
        "The server could not attach the barcode (HTTP 500). Try again.",
      ),
    ).toBeOnTheScreen();
  });

  it("says so when there is no barcode to attach", async () => {
    await renderAttach("/attach-barcode");

    expect(
      await screen.findByText("There is no barcode to attach. Scan one first."),
    ).toBeOnTheScreen();
    expect(screen.queryByLabelText("Search")).toBeNull();
  });
});
