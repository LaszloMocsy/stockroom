import { beforeEach, describe, expect, it } from "@jest/globals";

import {
  createAppStorage,
  StorageKeys,
  type AppStorage,
  type KeyValueStore,
} from "./storage";

/** An in-memory store that rejects keys the way expo-secure-store does. */
class FakeStore implements KeyValueStore {
  readonly items = new Map<string, string>();

  async getItem(key: string) {
    FakeStore.check(key);
    return this.items.get(key) ?? null;
  }

  async setItem(key: string, value: string) {
    FakeStore.check(key);
    this.items.set(key, value);
  }

  async deleteItem(key: string) {
    FakeStore.check(key);
    this.items.delete(key);
  }

  private static check(key: string) {
    if (!/^[\w.-]+$/.test(key)) {
      throw new Error(`Invalid key "${key}"`);
    }
  }
}

const tokens = { accessToken: "access-1", refreshToken: "refresh-1" };

describe("app storage", () => {
  let store: FakeStore;
  let storage: AppStorage;

  beforeEach(() => {
    store = new FakeStore();
    storage = createAppStorage(store);
  });

  it("starts with no server and no tokens", async () => {
    expect(await storage.getServerUrl()).toBeNull();
    expect(await storage.getTokens()).toBeNull();
  });

  it("keeps the server URL", async () => {
    await storage.setServerUrl("https://stock.example.com");

    expect(await storage.getServerUrl()).toBe("https://stock.example.com");
  });

  it("keeps the tokens", async () => {
    await storage.setTokens(tokens);

    expect(await storage.getTokens()).toEqual(tokens);
  });

  it("replaces both tokens in a single entry", async () => {
    await storage.setTokens(tokens);
    await storage.setTokens({
      accessToken: "access-2",
      refreshToken: "refresh-2",
    });

    expect(await storage.getTokens()).toEqual({
      accessToken: "access-2",
      refreshToken: "refresh-2",
    });
    expect([...store.items.keys()]).toEqual([StorageKeys.tokens]);
  });

  it("signs out without forgetting the server", async () => {
    await storage.setServerUrl("https://stock.example.com");
    await storage.setTokens(tokens);

    await storage.clearTokens();

    expect(await storage.getTokens()).toBeNull();
    expect(await storage.getServerUrl()).toBe("https://stock.example.com");
  });

  it("forgets the server and the tokens", async () => {
    await storage.setServerUrl("https://stock.example.com");
    await storage.setTokens(tokens);

    await storage.clear();

    expect(await storage.getServerUrl()).toBeNull();
    expect(await storage.getTokens()).toBeNull();
    expect(store.items.size).toBe(0);
  });

  it.each([
    ["not JSON", "access-1"],
    ["not an object", "null"],
    ["missing a token", JSON.stringify({ accessToken: "access-1" })],
    [
      "a token that is not a string",
      JSON.stringify({ accessToken: "access-1", refreshToken: 1 }),
    ],
  ])("treats stored tokens that are %s as signed out", async (_, stored) => {
    store.items.set(StorageKeys.tokens, stored);

    expect(await storage.getTokens()).toBeNull();
  });
});
