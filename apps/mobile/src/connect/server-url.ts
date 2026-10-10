/** A scheme followed by `//`, as in `https://`. */
const SchemeWithSlashes = /^[a-z][a-z\d+.-]*:\/\//i;

/** Something before a colon that is not a port, as in `https:/host` or `ftp:host`: a mistyped or other scheme. */
const OtherScheme = /^[a-z][a-z\d+.-]*:(?!\d)/i;

/** The API's path prefix, which request paths include; users may paste it with the address. */
const ApiPath = /\/api\/v1$/i;

/**
 * Turns what the user typed into the server URL the app stores, or null when it is not a server address.
 * The scheme defaults to `https://`, and only `http` and `https` are accepted. A path is kept, for a
 * server behind a path prefix, but trailing slashes, a pasted `/api/v1`, the query, and the fragment are
 * dropped. Credentials are refused, since the URL is stored and shown in plain text.
 *
 * For example, ` Stock.Example.com/ ` becomes `https://stock.example.com`.
 */
export function normaliseServerUrl(input: string): string | null {
  const trimmed = input.trim();
  if (!trimmed) {
    return null;
  }
  if (!SchemeWithSlashes.test(trimmed) && OtherScheme.test(trimmed)) {
    return null;
  }

  let url: URL;
  try {
    url = new URL(
      SchemeWithSlashes.test(trimmed) ? trimmed : `https://${trimmed}`,
    );
  } catch {
    return null;
  }

  if (
    (url.protocol !== "https:" && url.protocol !== "http:") ||
    !url.hostname ||
    url.username ||
    url.password
  ) {
    return null;
  }

  const path = url.pathname.replace(/\/+$/, "").replace(ApiPath, "");
  return `${url.protocol}//${url.host}${path}`;
}

/** IPv4 loopback addresses, 127.0.0.0/8. */
const LoopbackIPv4 = /^127(?:\.\d{1,3}){3}$/;

/** Whether `hostname`, as `URL` gives it, is this device itself. */
function isLoopback(hostname: string): boolean {
  return (
    hostname === "localhost" ||
    hostname === "[::1]" ||
    LoopbackIPv4.test(hostname)
  );
}

/**
 * Whether the app may connect to the server at `url`, a URL from `normaliseServerUrl` (spec 11): over
 * `https://` always, and over unencrypted `http://` only to this device (`localhost`) or when
 * `allowHttp` is set, which development builds do.
 */
export function isAllowedServerUrl(url: string, allowHttp: boolean): boolean {
  const { protocol, hostname } = new URL(url);
  return protocol === "https:" || allowHttp || isLoopback(hostname);
}
