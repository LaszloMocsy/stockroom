import type { components } from "./generated/schema.js";

type ErrorResponse = components["schemas"]["ErrorResponse"];

/**
 * An error response from the API. The API answers errors with `{ error: { code, message, details } }`,
 * where `code` is stable and meant for programs; anything else, for example a proxy's 502 page, has a
 * null `code`.
 */
export class ApiResponseError extends Error {
  override readonly name = "ApiResponseError";

  constructor(
    /** The HTTP status, for example 404. */
    readonly status: number,
    /** The API's error code, for example `insufficient_stock`, or null when the body is not the API's. */
    readonly code: string | null,
    message: string,
    /** Extra data for some codes, for example the current value of a conflicting count. */
    readonly details: unknown = null,
    /**
     * How many seconds to wait before trying again, from the `Retry-After` header, for example after a
     * 429 from rate limiting or a login lockout; null without one.
     */
    readonly retryAfter: number | null = null,
  ) {
    super(message);
  }

  /**
   * Builds the error from a response's status and its parsed body, as openapi-fetch returns them, and
   * optionally its headers.
   */
  static from(
    status: number,
    body: unknown,
    headers?: Headers,
  ): ApiResponseError {
    const retryAfter = parseRetryAfter(headers?.get("Retry-After") ?? null);
    if (isErrorResponse(body)) {
      const { code, message, details } = body.error;
      return new ApiResponseError(status, code, message, details, retryAfter);
    }
    return new ApiResponseError(
      status,
      null,
      `HTTP ${status}`,
      null,
      retryAfter,
    );
  }
}

/**
 * Reads a `Retry-After` header as a number of seconds. The API sends whole seconds; an HTTP date, which
 * a proxy may send instead, counts from now.
 */
function parseRetryAfter(value: string | null): number | null {
  if (value === null) {
    return null;
  }
  if (/^\d+$/.test(value.trim())) {
    return Number(value.trim());
  }
  const date = Date.parse(value);
  return Number.isNaN(date)
    ? null
    : Math.max(0, Math.ceil((date - Date.now()) / 1000));
}

/**
 * Returns the data of a successful request, or throws an `ApiResponseError` for an error response, so a
 * request can serve as a TanStack Query function:
 * `queryFn: () => unwrap(client.GET("/api/v1/info"))`. Network failures reject as they are.
 */
export async function unwrap<Data>(
  request: Promise<{ data?: Data; error?: unknown; response: Response }>,
): Promise<Data> {
  const { data, error, response } = await request;
  if (!response.ok) {
    throw ApiResponseError.from(response.status, error, response.headers);
  }
  // Undefined only for a response without a body, such as 204, whose type is then undefined too.
  return data as Data;
}

function isErrorResponse(body: unknown): body is ErrorResponse {
  if (typeof body !== "object" || body === null || !("error" in body)) {
    return false;
  }
  const { error } = body;
  return (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    "message" in error &&
    typeof error.code === "string" &&
    typeof error.message === "string"
  );
}
