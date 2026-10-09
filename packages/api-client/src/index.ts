import type { components } from "./generated/schema.js";

export type { components, operations, paths } from "./generated/schema.js";
export {
  ClientHeaders,
  createApiClient,
  normaliseBaseUrl,
  type ApiClient,
  type ApiClientOptions,
  type TokenRefreshOptions,
  type TokenResponse,
} from "./client.js";

/** A schema from the API contract by name, for example `Schema<"ProductResponse">`. */
export type Schema<Name extends keyof components["schemas"]> =
  components["schemas"][Name];
