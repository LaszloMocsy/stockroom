import { describe, expect, it } from "@jest/globals";
import { execFileSync } from "node:child_process";

/**
 * The app's native configuration after Expo's config plugins, as `expo prebuild` would write it, read
 * with `expo config --type introspect` so that no native project has to be generated.
 */
function introspectConfig() {
  const output = execFileSync(
    process.execPath,
    [
      require.resolve("expo/bin/cli"),
      "config",
      "--type",
      "introspect",
      "--json",
    ],
    { cwd: __dirname, encoding: "utf8" },
  );
  return JSON.parse(output) as {
    _internal: {
      modResults: {
        android: {
          manifest: {
            manifest: { application: { $: Record<string, string> }[] };
          };
        };
      };
    };
  };
}

describe("native configuration", () => {
  // Release builds use only the main manifest. Expo's template overrides the attribute in the debug
  // variants' manifests, which development builds use, so that they reach a local API over HTTP.
  it("does not allow cleartext traffic in Android's main manifest", () => {
    const [application] =
      introspectConfig()._internal.modResults.android.manifest.manifest
        .application;

    expect(application?.$["android:usesCleartextTraffic"]).toBe("false");
  }, 30_000);
});
