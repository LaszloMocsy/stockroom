import createClient, { type Client, type ClientOptions } from "openapi-fetch";
import type { components, paths } from "./generated/schema.js";

export type { components, operations, paths } from "./generated/schema.js";

/** A schema from the API contract by name, for example `Schema<"ProductResponse">`. */
export type Schema<Name extends keyof components["schemas"]> =
  components["schemas"][Name];

/** A fetch client whose paths, parameters, request bodies, and responses are typed from the API contract. */
export type ApiClient = Client<paths>;

export type ApiClientOptions = ClientOptions;

/**
 * Creates a typed client for the Stockroom API. Paths are the full API paths, for example
 * `client.GET("/api/v1/products/{id}", { params: { path: { id } } })`.
 */
export function createApiClient(options: ApiClientOptions = {}): ApiClient {
  return createClient<paths>(options);
}
