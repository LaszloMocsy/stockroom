import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
import { act, render, screen } from "@testing-library/react-native";

import { ApiProvider } from "@/api/provider";
import i18n from "@/i18n";
import { MemoryStore } from "@/storage/memory-store";
import {
  createAppStorage,
  type AppStorage,
  type KeyValueStore,
} from "@/storage/storage";

import { ServerStatus } from "./server-status";

const info = {
  server_version: "0.1.0",
  api_version: "1.0",
  min_client_version: "0.0.0",
  setup_required: false,
};

let fetch: jest.SpiedFunction<typeof globalThis.fetch>;

beforeEach(() => {
  fetch = jest
    .spyOn(globalThis, "fetch")
    .mockImplementation(async () => Response.json(info));
});

afterEach(() => {
  fetch.mockRestore();
});

async function renderStatus(
  storage: AppStorage,
  defaultServerUrl: string | null = "http://localhost:5278",
) {
  await render(
    <ApiProvider storage={storage} defaultServerUrl={defaultServerUrl}>
      <ServerStatus />
    </ApiProvider>,
  );
}

/** The URL of the request that the n-th fetch call sent. */
const requestUrl = (call = 0) => (fetch.mock.calls[call]![0] as Request).url;

describe("ServerStatus", () => {
  it("fetches /info from the stored server", async () => {
    const storage = createAppStorage(new MemoryStore());
    await storage.setServerUrl("https://stock.example.com");

    await renderStatus(storage);

    expect(
      await screen.findByText(
        "Connected to https://stock.example.com: server 0.1.0, API 1.0",
      ),
    ).toBeOnTheScreen();
    expect(requestUrl()).toBe("https://stock.example.com/api/v1/info");
  });

  it("uses the default server while none is stored", async () => {
    await renderStatus(createAppStorage(new MemoryStore()));

    expect(
      await screen.findByText(
        "Connected to http://localhost:5278: server 0.1.0, API 1.0",
      ),
    ).toBeOnTheScreen();
    expect(requestUrl()).toBe("http://localhost:5278/api/v1/info");
  });

  it("uses the default server when the stored one cannot be read", async () => {
    const broken: KeyValueStore = {
      getItem: () => Promise.reject(new Error("Keychain unavailable")),
      setItem: () => Promise.resolve(),
      deleteItem: () => Promise.resolve(),
    };

    await renderStatus(createAppStorage(broken));

    expect(
      await screen.findByText(
        "Connected to http://localhost:5278: server 0.1.0, API 1.0",
      ),
    ).toBeOnTheScreen();
  });

  it("says so when there is no server", async () => {
    await renderStatus(createAppStorage(new MemoryStore()), null);

    expect(await screen.findByText("No server is set.")).toBeOnTheScreen();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("shows the API's error message", async () => {
    fetch.mockImplementation(async () =>
      Response.json(
        { error: { code: "not_found", message: "Not found.", details: null } },
        { status: 404 },
      ),
    );

    await renderStatus(createAppStorage(new MemoryStore()));

    expect(
      await screen.findByText("Cannot reach http://localhost:5278: Not found."),
    ).toBeOnTheScreen();
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("takes its text from translation keys", async () => {
    // In i18next's "cimode", text is shown as its key.
    await i18n.changeLanguage("cimode");
    try {
      await renderStatus(createAppStorage(new MemoryStore()));

      expect(
        await screen.findByText("serverStatus.connected"),
      ).toBeOnTheScreen();
    } finally {
      // Re-renders the component, so inside act.
      await act(() => i18n.changeLanguage("en"));
    }
  });
});
