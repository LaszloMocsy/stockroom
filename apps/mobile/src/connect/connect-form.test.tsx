import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { fireEvent, render, screen } from "@testing-library/react-native";
import { Text } from "react-native";

import { clientVersion } from "@/api/client";
import { ApiProvider, useServerUrl } from "@/api/provider";
import { MemoryStore } from "@/storage/memory-store";
import {
  createAppStorage,
  StorageKeys,
  type AppStorage,
} from "@/storage/storage";

import { ConnectForm } from "./connect-form";

const info = {
  server_version: "0.1.0",
  api_version: "1.0",
  min_client_version: "0.0.0",
  setup_required: false,
};

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;
let store: MemoryStore;
let storage: AppStorage;

beforeEach(() => {
  fetch = jest
    .spyOn(globalThis, "fetch")
    .mockImplementation(async () => Response.json(info));
  store = new MemoryStore();
  storage = createAppStorage(store);
});

afterEach(() => {
  fetch.mockRestore();
});

/** Shows the server the provider is using, to see the switch to a newly stored one. */
function CurrentServer() {
  return <Text testID="current-server">{useServerUrl() ?? "none"}</Text>;
}

async function renderForm({
  initialAddress = null,
  allowHttp = false,
}: { initialAddress?: string | null; allowHttp?: boolean } = {}) {
  await render(
    <ApiProvider storage={storage}>
      <ConnectForm initialAddress={initialAddress} allowHttp={allowHttp} />
      <CurrentServer />
    </ApiProvider>,
  );
}

async function connectTo(address: string) {
  await fireEvent.changeText(
    await screen.findByLabelText("Server address"),
    address,
  );
  await fireEvent.press(screen.getByRole("button", { name: "Connect" }));
}

/** The URL of the request that the n-th fetch call sent. */
const requestUrl = (call = 0) => (fetch.mock.calls[call]![0] as Request).url;

describe("ConnectForm", () => {
  it("checks the normalised address and stores it", async () => {
    await renderForm();

    await connectTo(" Stock.Example.com/ ");

    expect(
      await screen.findByText("https://stock.example.com"),
    ).toBeOnTheScreen();
    expect(requestUrl()).toBe("https://stock.example.com/api/v1/info");
    expect(store.items.get(StorageKeys.serverUrl)).toBe(
      "https://stock.example.com",
    );
  });

  it("starts with the initial address", async () => {
    await renderForm({ initialAddress: "http://localhost:5278" });

    expect(
      await screen.findByDisplayValue("http://localhost:5278"),
    ).toBeOnTheScreen();
  });

  it("refuses a malformed address without a request", async () => {
    await renderForm();

    await connectTo("ftp://stock.example.com");

    expect(
      await screen.findByRole("alert", {
        name: "This is not a server address. Enter one like stock.example.com or https://stock.example.com.",
      }),
    ).toBeOnTheScreen();
    expect(fetch).not.toHaveBeenCalled();
    expect(screen.getByTestId("current-server")).toHaveTextContent("none");
  });

  it("refuses an http:// server without a request", async () => {
    await renderForm();

    await connectTo("http://stock.example.com");

    expect(
      await screen.findByText(
        "http://stock.example.com is not encrypted. Use the server's https:// address.",
      ),
    ).toBeOnTheScreen();
    expect(fetch).not.toHaveBeenCalled();
    expect(store.items.has(StorageKeys.serverUrl)).toBe(false);
  });

  it("accepts an http:// server on this device", async () => {
    await renderForm();

    await connectTo("http://localhost:5278");

    expect(await screen.findByText("http://localhost:5278")).toBeOnTheScreen();
  });

  it("accepts any http:// server where HTTP is allowed", async () => {
    await renderForm({ allowHttp: true });

    await connectTo("http://192.168.1.20:5278");

    expect(
      await screen.findByText("http://192.168.1.20:5278"),
    ).toBeOnTheScreen();
  });

  it("says when the server cannot be reached", async () => {
    fetch.mockRejectedValue(new TypeError("Network request failed"));
    await renderForm();

    await connectTo("stock.example.com");

    expect(
      await screen.findByText(
        "Cannot reach https://stock.example.com. Check the address and your network connection.",
      ),
    ).toBeOnTheScreen();
    expect(store.items.has(StorageKeys.serverUrl)).toBe(false);
  });

  it("says when the server answers with an error", async () => {
    fetch.mockImplementation(
      async () => new Response("Bad gateway", { status: 502 }),
    );
    await renderForm();

    await connectTo("stock.example.com");

    expect(
      await screen.findByText(
        "The server at https://stock.example.com answered with an error (HTTP 502). Try again later.",
      ),
    ).toBeOnTheScreen();
  });

  it("says when the server is not Stockroom", async () => {
    fetch.mockImplementation(
      async () => new Response("<!doctype html><title>Welcome</title>"),
    );
    await renderForm();

    await connectTo("example.com");

    expect(
      await screen.findByText(
        "https://example.com is not a Stockroom server. Check the address.",
      ),
    ).toBeOnTheScreen();
    expect(store.items.has(StorageKeys.serverUrl)).toBe(false);
    expect(screen.getByTestId("current-server")).toHaveTextContent("none");
  });

  it("says when the server's versions are unreadable", async () => {
    fetch.mockImplementation(async () =>
      Response.json({ ...info, api_version: "latest" }),
    );
    await renderForm();

    await connectTo("stock.example.com");

    expect(
      await screen.findByText(
        "https://stock.example.com is not a Stockroom server. Check the address.",
      ),
    ).toBeOnTheScreen();
  });

  it("refuses a server that needs a newer app", async () => {
    fetch.mockImplementation(async () =>
      Response.json({ ...info, min_client_version: "99.0.0" }),
    );
    await renderForm();

    await connectTo("stock.example.com");

    expect(
      await screen.findByText(
        `https://stock.example.com needs a newer version of the app than ${clientVersion}. Please update the app.`,
      ),
    ).toBeOnTheScreen();
    expect(store.items.has(StorageKeys.serverUrl)).toBe(false);
    expect(screen.getByTestId("current-server")).toHaveTextContent("none");
  });

  it("refuses a server that is outdated", async () => {
    fetch.mockImplementation(async () =>
      Response.json({ ...info, api_version: "0.9" }),
    );
    await renderForm();

    await connectTo("stock.example.com");

    expect(
      await screen.findByText(
        "The server at https://stock.example.com is outdated. Ask your administrator to update it.",
      ),
    ).toBeOnTheScreen();
    expect(store.items.has(StorageKeys.serverUrl)).toBe(false);
  });

  it("says when the address cannot be stored", async () => {
    jest
      .spyOn(store, "setItem")
      .mockRejectedValue(new Error("Keychain unavailable"));
    await renderForm();

    await connectTo("stock.example.com");

    expect(
      await screen.findByText(
        "The server address could not be saved. Try again.",
      ),
    ).toBeOnTheScreen();
    expect(screen.getByTestId("current-server")).toHaveTextContent("none");
  });

  it("clears the error when the address changes", async () => {
    await renderForm();
    await connectTo("ftp://stock.example.com");
    expect(await screen.findByRole("alert")).toBeOnTheScreen();

    await fireEvent.changeText(
      screen.getByLabelText("Server address"),
      "stock.example.com",
    );

    expect(screen.queryByRole("alert")).not.toBeOnTheScreen();
  });
});
