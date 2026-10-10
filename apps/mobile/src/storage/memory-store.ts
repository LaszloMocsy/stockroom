import type { KeyValueStore } from "./storage";

/** A store in memory for tests. Like expo-secure-store, it rejects keys with other characters than letters, digits, `.`, `-`, and `_`. */
export class MemoryStore implements KeyValueStore {
  readonly items = new Map<string, string>();

  async getItem(key: string) {
    MemoryStore.check(key);
    return this.items.get(key) ?? null;
  }

  async setItem(key: string, value: string) {
    MemoryStore.check(key);
    this.items.set(key, value);
  }

  async deleteItem(key: string) {
    MemoryStore.check(key);
    this.items.delete(key);
  }

  private static check(key: string) {
    if (!/^[\w.-]+$/.test(key)) {
      throw new Error(`Invalid key "${key}"`);
    }
  }
}
