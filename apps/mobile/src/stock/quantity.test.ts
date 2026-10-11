import { describe, expect, it } from "@jest/globals";

import { quantityError, stepQuantity } from "./quantity";

describe("quantityError", () => {
  it.each([
    ["1", 1],
    [" 12 ", 1],
    ["0", 0],
    ["1000000", 1],
  ])("accepts %j from %i", (text, min) => {
    expect(quantityError(text, min)).toBeUndefined();
  });

  it.each([
    ["", 1, "required"],
    [" ", 1, "required"],
    ["-1", 0, "wholeNumber"],
    ["2.5", 1, "wholeNumber"],
    ["1e3", 1, "wholeNumber"],
    ["0", 1, "tooSmall"],
    ["1000001", 1, "tooLarge"],
  ])("refuses %j from %i as %s", (text, min, error) => {
    expect(quantityError(text, min)).toBe(error);
  });
});

describe("stepQuantity", () => {
  it("adds and takes off one", () => {
    expect(stepQuantity("4", 1, 1)).toBe("5");
    expect(stepQuantity(" 4 ", -1, 1)).toBe("3");
  });

  it("stays from the minimum to the largest quantity", () => {
    expect(stepQuantity("1", -1, 1)).toBe("1");
    expect(stepQuantity("0", 1, 1)).toBe("1");
    expect(stepQuantity("1000000", 1, 1)).toBe("1000000");
  });

  it("starts again from the minimum when the text is not a whole number", () => {
    expect(stepQuantity("", 1, 1)).toBe("1");
    expect(stepQuantity("2.5", -1, 0)).toBe("0");
  });
});
