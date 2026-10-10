import * as SecureStore from "expo-secure-store";

import type { KeyValueStore } from "./storage";

/** The device's secure storage, through `expo-secure-store`. */
export const secureStore: KeyValueStore = {
  getItem: (key) => SecureStore.getItemAsync(key),
  setItem: (key, value) => SecureStore.setItemAsync(key, value),
  deleteItem: (key) => SecureStore.deleteItemAsync(key),
};
