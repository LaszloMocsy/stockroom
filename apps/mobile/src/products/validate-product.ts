import type { Schema } from "@stockroom/api-client";

export interface NewProductValues {
  name: string;
  sku: string;
  minStock: string;
  barcode: string;
  initialQuantity: string;
}

export type NewProductField = keyof NewProductValues;

/** Why a field is not accepted, as a key under `createProduct.errors` in the translations. */
export type NewProductFieldError = "required" | "wholeNumber" | "tooLarge";

/** The server's largest quantity, checked here too so that the message can be translated. */
export const MaxQuantity = 1_000_000;

export const MaxLengths = {
  name: 200,
  sku: 64,
  barcode: 512,
} as const;

/** A whole number of zero or more, as typed: digits only, so no sign, decimals, or exponent. */
const WholeNumberPattern = /^\d+$/;

function validateQuantity(text: string): NewProductFieldError | undefined {
  const trimmed = text.trim();
  if (trimmed === "") {
    return undefined;
  }
  if (!WholeNumberPattern.test(trimmed)) {
    return "wholeNumber";
  }
  return Number(trimmed) > MaxQuantity ? "tooLarge" : undefined;
}

/**
 * Checks the new product form before it is sent, returning an error for each field that the server
 * would refuse. Only the name is required; the quantities are optional whole numbers.
 */
export function validateNewProduct(
  values: NewProductValues,
): Partial<Record<NewProductField, NewProductFieldError>> {
  const errors: Partial<Record<NewProductField, NewProductFieldError>> = {};
  if (!values.name.trim()) {
    errors.name = "required";
  }
  const minStock = validateQuantity(values.minStock);
  if (minStock) {
    errors.minStock = minStock;
  }
  const initialQuantity = validateQuantity(values.initialQuantity);
  if (initialQuantity) {
    errors.initialQuantity = initialQuantity;
  }
  return errors;
}

/**
 * The request for valid form values. Blank optional fields are left out, so the server generates the
 * SKU and the product starts with no minimum, no barcode, and nothing on hand. The barcode is sent as
 * it is, not trimmed, because scans look it up exactly.
 */
export function newProductRequest(
  values: NewProductValues,
): Schema<"CreateProductRequest"> {
  const sku = values.sku.trim();
  const minStock = values.minStock.trim();
  const initialQuantity = values.initialQuantity.trim();
  return {
    name: values.name.trim(),
    ...(sku && { sku }),
    ...(minStock && { min_stock: Number(minStock) }),
    ...(values.barcode.trim() && { barcode: values.barcode }),
    ...(initialQuantity && { initial_quantity: Number(initialQuantity) }),
  };
}
