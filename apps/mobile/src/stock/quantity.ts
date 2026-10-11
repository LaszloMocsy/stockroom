import { MaxQuantity } from "@/products/validate-product";

/** Why a quantity is not accepted, as a key under `quantity.errors` in the translations. */
export type QuantityError =
  "required" | "wholeNumber" | "tooSmall" | "tooLarge";

/** A whole number, as typed: digits only, so no sign, decimals, or exponent. */
const WholeNumberPattern = /^\d+$/;

/** Why `text` is not a whole number from `min` to the server's largest quantity, if it is not. */
export function quantityError(
  text: string,
  min: number,
): QuantityError | undefined {
  const trimmed = text.trim();
  if (trimmed === "") {
    return "required";
  }
  if (!WholeNumberPattern.test(trimmed)) {
    return "wholeNumber";
  }
  const quantity = Number(trimmed);
  if (quantity < min) {
    return "tooSmall";
  }
  return quantity > MaxQuantity ? "tooLarge" : undefined;
}

/**
 * `text` changed by `step`, kept from `min` to the server's largest quantity. Text that is not a whole
 * number steps to `min`.
 */
export function stepQuantity(text: string, step: number, min: number): string {
  const trimmed = text.trim();
  const quantity = WholeNumberPattern.test(trimmed)
    ? Number(trimmed) + step
    : min;
  return String(Math.min(Math.max(quantity, min), MaxQuantity));
}
