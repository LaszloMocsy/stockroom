import { describe, expect, it, jest } from "@jest/globals";
import * as SecureStore from "expo-secure-store";

import { secureStore } from "./secure-store";

jest.mock("expo-secure-store");

const mocked = jest.mocked(SecureStore);

describe("secureStore", () => {
  it("reads, writes, and deletes through expo-secure-store", async () => {
    mocked.getItemAsync.mockResolvedValue("value");

    expect(await secureStore.getItem("stockroom.key")).toBe("value");
    await secureStore.setItem("stockroom.key", "new value");
    await secureStore.deleteItem("stockroom.key");

    expect(mocked.getItemAsync).toHaveBeenCalledWith("stockroom.key");
    expect(mocked.setItemAsync).toHaveBeenCalledWith(
      "stockroom.key",
      "new value",
    );
    expect(mocked.deleteItemAsync).toHaveBeenCalledWith("stockroom.key");
  });
});
