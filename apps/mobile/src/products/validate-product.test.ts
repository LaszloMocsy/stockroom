import { describe, expect, it } from "@jest/globals";

import type { Schema } from "@stockroom/api-client";

import {
  newProductRequest,
  productEditValues,
  productUpdateRequest,
  validateNewProduct,
  validateProductEdit,
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

const cableTies: Schema<"ProductResponse"> = {
  id: "0b8e2c1a-5d7f-4e3b-9a61-2f4c8d9e1a37",
  sku: "WRK-CBT-200",
  name: "Cable ties",
  description: "Black.",
  barcodes: [],
  min_stock: 10,
  quantity: 2,
  archived_at: null,
  created_at: "2026-10-01T08:00:00Z",
  updated_at: "2026-10-01T08:00:00Z",
};

describe("validateProductEdit", () => {
  it("requires a name, and a whole number as the minimum", () => {
    expect(
      validateProductEdit({ name: " ", description: "", minStock: "2.5" }),
    ).toEqual({ name: "required", minStock: "wholeNumber" });
  });

  it("accepts no description and no minimum", () => {
    expect(
      validateProductEdit({
        name: "Cable ties",
        description: "",
        minStock: "",
      }),
    ).toEqual({});
  });
});

describe("productUpdateRequest", () => {
  it("sends nothing when nothing changed", () => {
    expect(
      productUpdateRequest(productEditValues(cableTies), cableTies),
    ).toEqual({});
  });

  it("sends only the changed fields, trimmed", () => {
    expect(
      productUpdateRequest(
        { name: " Cable ties, black ", description: "Black.", minStock: "12" },
        cableTies,
      ),
    ).toEqual({ name: "Cable ties, black", min_stock: 12 });
  });

  it("removes a description or minimum that is cleared", () => {
    expect(
      productUpdateRequest(
        { name: "Cable ties", description: "  ", minStock: "" },
        cableTies,
      ),
    ).toEqual({ description: null, min_stock: null });
  });

  it("sets a description and minimum the product did not have", () => {
    expect(
      productUpdateRequest(
        { name: "Cable ties", description: "UV resistant", minStock: "0" },
        { ...cableTies, description: null, min_stock: null },
      ),
    ).toEqual({ description: "UV resistant", min_stock: 0 });
  });
});
