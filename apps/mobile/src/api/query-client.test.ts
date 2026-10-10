import { describe, expect, it } from "@jest/globals";
import { ApiResponseError } from "@stockroom/api-client";

import { shouldRetry } from "./query-client";

const answer = (status: number) =>
  new ApiResponseError(status, null, `HTTP ${status}`);

describe("shouldRetry", () => {
  it.each([400, 401, 404, 429])("does not retry a %i", (status) => {
    expect(shouldRetry(0, answer(status))).toBe(false);
  });

  it.each([
    ["a server error", answer(503)],
    ["a network failure", new TypeError("Network request failed")],
  ])("retries %s up to three times", (_, error) => {
    expect(shouldRetry(0, error)).toBe(true);
    expect(shouldRetry(2, error)).toBe(true);
    expect(shouldRetry(3, error)).toBe(false);
  });
});
