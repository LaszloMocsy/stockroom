import { describe, expect, it } from "@jest/globals";

import {
  newProductRequest,
  validateNewProduct,
  type NewProductValues,
} from "./validate-product";

const empty: NewProductValues = {
  name: "",
  sku: "",
  minStock: "",
  barcode: "",
  initialQuantity: "",
};

describe("validateNewProduct", () => {
  it("accepts a name alone", () => {
    expect(validateNewProduct({ ...empty, name: "Cable ties" })).toEqual({});
  });

  it("requires a name that is not blank", () => {
    expect(validateNewProduct({ ...empty, name: "  " })).toEqual({
      name: "required",
    });
  });

  it("accepts whole numbers from 0 to 1,000,000 as quantities", () => {
    expect(
      validateNewProduct({
        ...empty,
        name: "Cable ties",
        minStock: "0",
        initialQuantity: " 1000000 ",
      }),
    ).toEqual({});
  });

  it.each(["-1", "2.5", "1e3", "ten", "1 000"])(
    "refuses %p as a quantity",
    (text) => {
      expect(
        validateNewProduct({
          ...empty,
          name: "Cable ties",
          minStock: text,
          initialQuantity: text,
        }),
      ).toEqual({ minStock: "wholeNumber", initialQuantity: "wholeNumber" });
    },
  );

  it("refuses quantities above 1,000,000", () => {
    expect(
      validateNewProduct({
        ...empty,
        name: "Cable ties",
        minStock: "1000001",
        initialQuantity: "1000001",
      }),
    ).toEqual({ minStock: "tooLarge", initialQuantity: "tooLarge" });
  });
});

describe("newProductRequest", () => {
  it("leaves out blank optional fields", () => {
    expect(
      newProductRequest({
        ...empty,
        name: " Cable ties ",
        sku: " ",
        barcode: " ",
      }),
    ).toEqual({ name: "Cable ties" });
  });

  it("sends every field that is filled in, with numbers as numbers", () => {
    expect(
      newProductRequest({
        name: "Cable ties",
        sku: " WRK-CBT ",
        minStock: "10",
        barcode: "4006381333931",
        initialQuantity: "0",
      }),
    ).toEqual({
      name: "Cable ties",
      sku: "WRK-CBT",
      min_stock: 10,
      barcode: "4006381333931",
      initial_quantity: 0,
    });
  });

  it("sends the barcode exactly as it is", () => {
    expect(
      newProductRequest({ ...empty, name: "Box", barcode: " QR payload " }),
    ).toMatchObject({ barcode: " QR payload " });
  });
});
