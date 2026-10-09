import { describe, expect, it } from "vitest";
import {
  checkCompatibility,
  RequiredApiVersion,
  type InfoResponse,
} from "./index.js";

function info(overrides: Partial<InfoResponse> = {}): InfoResponse {
  return {
    server_version: "0.1.0",
    api_version: "1.0",
    min_client_version: "0.0.0",
    setup_required: false,
    ...overrides,
  };
}

describe("checkCompatibility", () => {
  it("requires API 1.0 by default", () => {
    expect(RequiredApiVersion).toBe("1.0");
  });

  describe("ok", () => {
    it.each([
      ["any client version is accepted", info(), "0.1.0"],
      [
        "a prerelease app is accepted by a server that accepts any version",
        info(),
        "0.0.0-dev",
      ],
      [
        "the app is exactly the minimum",
        info({ min_client_version: "1.2.0" }),
        "1.2.0",
      ],
      [
        "the app is newer than the minimum",
        info({ min_client_version: "1.2.0" }),
        "1.10.0",
      ],
      [
        "the server's API has a newer minor",
        info({ api_version: "1.3" }),
        "1.0.0",
      ],
      [
        "build metadata is ignored",
        info({ min_client_version: "1.2.0" }),
        "1.2.0+build.42",
      ],
    ])("when %s", (_, server, clientVersion) => {
      expect(checkCompatibility(server, clientVersion)).toBe("ok");
    });

    it("when the server's API is exactly what the app requires", () => {
      expect(
        checkCompatibility(info({ api_version: "1.2" }), "1.0.0", "1.2"),
      ).toBe("ok");
    });
  });

  describe("app_outdated", () => {
    it.each([
      ["older patch", "1.2.2", "1.2.3"],
      ["older minor, compared as numbers", "1.9.9", "1.10.0"],
      ["older major", "0.9.0", "1.0.0"],
      ["prerelease of the minimum", "1.2.0-beta.1", "1.2.0"],
      ["earlier prerelease", "1.2.0-beta.2", "1.2.0-beta.10"],
      ["numeric before alphanumeric prerelease", "1.2.0-1", "1.2.0-alpha"],
    ])("when the app is an %s", (_, clientVersion, minClientVersion) => {
      expect(
        checkCompatibility(
          info({ min_client_version: minClientVersion }),
          clientVersion,
        ),
      ).toBe("app_outdated");
    });

    it("when the server's API has a newer major version", () => {
      expect(checkCompatibility(info({ api_version: "2.0" }), "1.0.0")).toBe(
        "app_outdated",
      );
    });

    it("before server_outdated, since the user can update the app themselves", () => {
      expect(
        checkCompatibility(
          info({ api_version: "1.0", min_client_version: "2.0.0" }),
          "1.0.0",
          "1.1",
        ),
      ).toBe("app_outdated");
    });
  });

  describe("server_outdated", () => {
    it.each([
      ["an older minor", "1.1", "1.2"],
      ["an older minor, compared as numbers", "1.9", "1.10"],
      ["an older major", "1.4", "2.0"],
    ])("when the server's API is %s", (_, apiVersion, required) => {
      expect(
        checkCompatibility(
          info({ api_version: apiVersion }),
          "1.0.0",
          required,
        ),
      ).toBe("server_outdated");
    });
  });

  it.each([
    ["client version", info(), "latest"],
    ["client version with a leading v", info(), "v1.0.0"],
    ["api_version", info({ api_version: "one" }), "1.0.0"],
    ["min_client_version", info({ min_client_version: "1.x" }), "1.0.0"],
  ])("throws for a malformed %s", (_, server, clientVersion) => {
    expect(() => checkCompatibility(server, clientVersion)).toThrow(TypeError);
  });
});
