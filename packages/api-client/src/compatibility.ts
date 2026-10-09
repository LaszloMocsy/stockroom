import type { components } from "./generated/schema.js";

export type InfoResponse = components["schemas"]["InfoResponse"];

/**
 * - `ok`: the app and the server can work together.
 * - `app_outdated`: the app is older than the server's `min_client_version`; ask the user to update the app.
 * - `server_outdated`: the server's API is older than the app needs; ask an administrator to update the server.
 */
export type Compatibility = "ok" | "app_outdated" | "server_outdated";

/**
 * The oldest API version, as `major.minor`, that this client works with. The server reports its own as
 * `api_version` and raises the minor with each additive change. Raise this when the client starts using
 * an endpoint or field added in a later minor version.
 */
export const RequiredApiVersion = "1.0";

/** `min_client_version` value with which the server accepts any client. */
const AnyClientVersion = "0.0.0";

/**
 * Decides whether this app can talk to the server described by `GET /api/v1/info` (spec 10.1). Check on
 * connect and after an app or server update. An outdated app takes precedence, since the user can fix
 * that themselves.
 *
 * @param clientVersion The app's semantic version, for example `1.4.0`.
 * @throws {TypeError} When a version is not a semantic version, for example when the URL is not a Stockroom server.
 */
export function checkCompatibility(
  info: Pick<InfoResponse, "api_version" | "min_client_version">,
  clientVersion: string,
  requiredApiVersion: string = RequiredApiVersion,
): Compatibility {
  const client = parseVersion(clientVersion, "client version");
  const serverApi = parseVersion(info.api_version, "api_version");
  const requiredApi = parseVersion(requiredApiVersion, "required API version");

  if (
    info.min_client_version !== AnyClientVersion &&
    compareVersions(
      client,
      parseVersion(info.min_client_version, "min_client_version"),
    ) < 0
  ) {
    return "app_outdated";
  }

  // A newer major API is a breaking change the app does not know; an older one lacks what it needs.
  if (serverApi.major > requiredApi.major) {
    return "app_outdated";
  }
  if (compareVersions(serverApi, requiredApi) < 0) {
    return "server_outdated";
  }
  return "ok";
}

interface Version {
  major: number;
  minor: number;
  patch: number;
  prerelease: string[];
}

// major.minor with an optional patch (api_version has none), prerelease, and build metadata.
const VersionPattern =
  /^(0|[1-9]\d*)\.(0|[1-9]\d*)(?:\.(0|[1-9]\d*))?(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$/;

function parseVersion(value: string, name: string): Version {
  const match = VersionPattern.exec(value.trim());
  if (!match) {
    throw new TypeError(`The ${name} "${value}" is not a semantic version.`);
  }
  return {
    major: Number(match[1]),
    minor: Number(match[2]),
    patch: Number(match[3] ?? 0),
    prerelease: match[4]?.split(".") ?? [],
  };
}

/** Semantic version precedence: negative if `a` is older than `b`, zero if equal, positive if newer. */
function compareVersions(a: Version, b: Version): number {
  return (
    a.major - b.major ||
    a.minor - b.minor ||
    a.patch - b.patch ||
    comparePrerelease(a.prerelease, b.prerelease)
  );
}

function comparePrerelease(a: string[], b: string[]): number {
  // A release is newer than any of its prereleases: 1.0.0-beta < 1.0.0.
  if (a.length === 0 || b.length === 0) {
    return b.length - a.length;
  }

  for (let i = 0; i < Math.min(a.length, b.length); i++) {
    const x = a[i]!;
    const y = b[i]!;
    if (x === y) {
      continue;
    }
    const xNumeric = /^\d+$/.test(x);
    const yNumeric = /^\d+$/.test(y);
    if (xNumeric && yNumeric) {
      return Number(x) - Number(y);
    }
    // Numeric identifiers sort before alphanumeric ones.
    if (xNumeric !== yNumeric) {
      return xNumeric ? -1 : 1;
    }
    return x < y ? -1 : 1;
  }
  return a.length - b.length;
}
