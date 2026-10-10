import { describe, expect, it } from "@jest/globals";

import { stockStatus } from "./stock-status";

describe("stockStatus", () => {
  it.each([
    { quantity: 0, min_stock: 5, status: "out" },
    { quantity: 0, min_stock: null, status: "out" },
    { quantity: -2, min_stock: null, status: "out" },
    { quantity: 5, min_stock: 5, status: "low" },
    { quantity: 4, min_stock: 5, status: "low" },
    { quantity: 6, min_stock: 5, status: "ok" },
    { quantity: 1, min_stock: null, status: "ok" },
    { quantity: 1, min_stock: 0, status: "ok" },
  ] as const)(
    "is $status with $quantity on hand and a minimum of $min_stock",
    ({ status, ...product }) => {
      expect(stockStatus(product)).toBe(status);
    },
  );
});
