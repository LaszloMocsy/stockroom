import { describe, expect, it } from "@jest/globals";

import { createScanFilter } from "./scan-filter";

describe("createScanFilter", () => {
  it("passes the first read of a barcode and drops the reads while it stays in view", () => {
    const filter = createScanFilter(1000);

    expect(filter.read("4006381333931", 0)).toBe(true);
    expect(filter.read("4006381333931", 100)).toBe(false);
    // Over a second after the first read, but each read keeps it in view.
    expect(filter.read("4006381333931", 900)).toBe(false);
    expect(filter.read("4006381333931", 1800)).toBe(false);
  });

  it("scans a barcode again once it has been out of view", () => {
    const filter = createScanFilter(1000);

    expect(filter.read("4006381333931", 0)).toBe(true);
    expect(filter.read("4006381333931", 999)).toBe(false);
    expect(filter.read("4006381333931", 1999)).toBe(true);
  });

  it("scans each barcode in view once", () => {
    const filter = createScanFilter(1000);

    expect(filter.read("4006381333931", 0)).toBe(true);
    expect(filter.read("0012345678905", 50)).toBe(true);
    expect(filter.read("4006381333931", 100)).toBe(false);
    expect(filter.read("0012345678905", 150)).toBe(false);
  });

  it("ignores empty reads", () => {
    const filter = createScanFilter(1000);

    expect(filter.read("", 0)).toBe(false);
    expect(filter.read("", 2000)).toBe(false);
  });

  it("does not scan a barcode still in view when the camera resumes", () => {
    const filter = createScanFilter(1000);
    expect(filter.read("4006381333931", 0)).toBe(true);

    // Paused for a minute, then resumed.
    filter.resume(60_000);

    expect(filter.read("4006381333931", 60_100)).toBe(false);
    // Taken away after resuming, then shown again.
    expect(filter.read("4006381333931", 61_100)).toBe(true);
  });
});
