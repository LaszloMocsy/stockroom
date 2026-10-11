import { describe, expect, it } from "@jest/globals";

import { clientVersion } from "@/api/client";

import { compatibilityWith } from "./compatibility";

describe("compatibilityWith", () => {
  it("accepts a server that accepts any app", () => {
    expect(
      compatibilityWith({ api_version: "1.0", min_client_version: "0.0.0" }),
    ).toBe("ok");
  });

  it("asks for an app update when the server needs a newer app", () => {
    expect(
      compatibilityWith({ api_version: "1.0", min_client_version: "99.0.0" }),
    ).toBe("app_outdated");
  });

  it("asks for a server update when its API is too old", () => {
    expect(
      compatibilityWith({ api_version: "0.9", min_client_version: "0.0.0" }),
    ).toBe("server_outdated");
  });

  it("compares with this app's version", () => {
    expect(
      compatibilityWith({
        api_version: "1.0",
        min_client_version: clientVersion,
      }),
    ).toBe("ok");
  });

  it("is unknown for versions that are not semantic versions", () => {
    expect(
      compatibilityWith({ api_version: "v1", min_client_version: "0.0.0" }),
    ).toBeNull();
  });
});
