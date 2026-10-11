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
  waitFor,
} from "@testing-library/react-native";
import { Alert } from "react-native";

import { AppProviders } from "@/navigation/app-providers";
import { MemoryStore } from "@/storage/memory-store";
import { createAppStorage } from "@/storage/storage";

import { ProductDetail } from "./product-detail";

const id = "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37";

/** The server's product, which archiving and restoring change. */
let product: Schema<"ProductResponse">;
/** The signed-in user's role. */
let role: "ADMIN" | "STAFF";
/** Answers archiving or restoring instead of the server when set. */
let changeResponse: (() => Response) | null;
let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let alert: jest.SpiedFunction<typeof Alert.alert>;

const errorResponse = (status: number, code: string) =>
  Response.json(
    { error: { code, message: "Refused.", details: null } },
    { status },
  );

/** Answers like the API, from `product` and `role`. */
async function serve(input: RequestInfo | URL): Promise<Response> {
  const request = input as Request;
  const route = `${request.method} ${new URL(request.url).pathname}`;
  switch (route) {
    case "GET /api/v1/me":
      return Response.json({
        id: "6f1c1a52-8a1e-4c63-9a54-1e1f4b8f2d10",
        username: "ada",
        display_name: "Ada Admin",
        role,
      });
    case `GET /api/v1/products/${id}`:
      return Response.json(product);
    case "GET /api/v1/stock/movements":
      return Response.json({ items: [], next_cursor: null });
    case `POST /api/v1/products/${id}/archive`:
    case `POST /api/v1/products/${id}/restore`:
      if (changeResponse) {
        return changeResponse();
      }
      product = {
        ...product,
        archived_at: route.endsWith("/archive") ? "2026-10-11T09:00:00Z" : null,
      };
      return Response.json(product);
    default:
      return new Response(null, { status: 404 });
  }
}

beforeEach(() => {
  product = {
    id,
    sku: "WRK-CBT-200",
    name: "Cable ties",
    description: null,
    barcodes: [],
    min_stock: null,
    quantity: 2,
    archived_at: null,
    created_at: "2026-10-01T08:00:00Z",
    updated_at: "2026-10-01T08:00:00Z",
  };
  role = "ADMIN";
  changeResponse = null;
  fetch = jest.spyOn(globalThis, "fetch").mockImplementation(serve);
  alert = jest.spyOn(Alert, "alert").mockImplementation(() => {});
});

afterEach(() => {
  fetch.mockRestore();
  alert.mockRestore();
});

async function renderProduct() {
  const storage = createAppStorage(new MemoryStore());
  await storage.setServerUrl("https://stock.example.com");
  await storage.setTokens({
    accessToken: "access-1",
    refreshToken: "refresh-1",
  });
  await render(
    <AppProviders storage={storage}>
      <ProductDetail id={id} />
    </AppProviders>,
  );
  await screen.findByRole("heading", { name: "Cable ties" });
}

/** How many times the signed-in user was loaded. */
const meRequests = () =>
  fetch.mock.calls.filter(
    ([input]) => new URL((input as Request).url).pathname === "/api/v1/me",
  ).length;

/** The requests that archived or restored the product. */
const changes = () =>
  fetch.mock.calls
    .map(([input]) => input as Request)
    .filter((request) => request.method === "POST")
    .map((request) => new URL(request.url).pathname.split("/").at(-1));

/** Answers the last confirmation with the button `text`. */
async function answerAlert(text: string) {
  const [, , buttons] = alert.mock.calls.at(-1)!;
  await act(() => buttons!.find((b) => b.text === text)?.onPress?.());
}

describe("ArchiveActions", () => {
  it("is hidden from STAFF users", async () => {
    role = "STAFF";
    await renderProduct();
    await waitFor(() => expect(meRequests()).toBe(1));
    await act(async () => {});

    expect(
      screen.queryByRole("button", { name: "Archive product" }),
    ).toBeNull();
  });

  it("archives the product once an ADMIN confirms", async () => {
    await renderProduct();

    await fireEvent.press(
      await screen.findByRole("button", { name: "Archive product" }),
    );
    expect(alert.mock.calls.at(-1)?.[1]).toBe(
      "Archive Cable ties? It will no longer show in the lists and search. Its stock and history stay, and an administrator can restore it.",
    );
    await answerAlert("Archive product");

    expect(
      await screen.findByText("This product is archived."),
    ).toBeOnTheScreen();
    expect(screen.getByText("Archived Cable ties.")).toBeOnTheScreen();
    expect(
      screen.getByRole("button", { name: "Restore product" }),
    ).toBeOnTheScreen();
    expect(changes()).toEqual(["archive"]);
  });

  it("does not archive when the confirmation is cancelled", async () => {
    await renderProduct();

    await fireEvent.press(
      await screen.findByRole("button", { name: "Archive product" }),
    );
    await answerAlert("Cancel");

    expect(changes()).toEqual([]);
  });

  it("restores an archived product", async () => {
    product = { ...product, archived_at: "2026-10-10T09:00:00Z" };
    await renderProduct();
    expect(screen.getByText("This product is archived.")).toBeOnTheScreen();

    await fireEvent.press(
      await screen.findByRole("button", { name: "Restore product" }),
    );

    expect(await screen.findByText("Restored Cable ties.")).toBeOnTheScreen();
    expect(screen.queryByText("This product is archived.")).toBeNull();
    expect(
      screen.getByRole("button", { name: "Archive product" }),
    ).toBeOnTheScreen();
    expect(changes()).toEqual(["restore"]);
  });

  it("hides the buttons when the server says the user is no longer an ADMIN", async () => {
    await renderProduct();
    await fireEvent.press(
      await screen.findByRole("button", { name: "Archive product" }),
    );
    role = "STAFF";
    changeResponse = () => errorResponse(403, "forbidden");
    await answerAlert("Archive product");

    expect(
      await screen.findByText(
        "Only administrators can archive and restore products.",
      ),
    ).toBeOnTheScreen();
    // The role loads again, and the buttons go.
    await waitFor(() =>
      expect(
        screen.queryByRole("button", { name: "Archive product" }),
      ).toBeNull(),
    );
    expect(meRequests()).toBe(2);
  });
});
