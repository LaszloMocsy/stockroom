import { describe, expect, it } from "@jest/globals";

import { createScanFilter } from "./scan-filter";

describe("createScanFilter", () => {
  it("passes the first read of a barcode and drops the reads while it stays in view", () => {
    const isNew = createScanFilter(1000);

    expect(isNew("4006381333931", 0)).toBe(true);
    expect(isNew("4006381333931", 100)).toBe(false);
    // Over a second after the first read, but each read keeps it in view.
    expect(isNew("4006381333931", 900)).toBe(false);
    expect(isNew("4006381333931", 1800)).toBe(false);
  });

  it("scans a barcode again once it has been out of view", () => {
    const isNew = createScanFilter(1000);

    expect(isNew("4006381333931", 0)).toBe(true);
    expect(isNew("4006381333931", 999)).toBe(false);
    expect(isNew("4006381333931", 1999)).toBe(true);
  });

  it("scans each barcode in view once", () => {
    const isNew = createScanFilter(1000);

    expect(isNew("4006381333931", 0)).toBe(true);
    expect(isNew("0012345678905", 50)).toBe(true);
    expect(isNew("4006381333931", 100)).toBe(false);
    expect(isNew("0012345678905", 150)).toBe(false);
  });

  it("ignores empty reads", () => {
    const isNew = createScanFilter(1000);

    expect(isNew("", 0)).toBe(false);
    expect(isNew("", 2000)).toBe(false);
  });
});
