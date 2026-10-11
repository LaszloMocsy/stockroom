import { describe, expect, it } from "@jest/globals";

import i18n, { pickLanguage } from "./index";

const locales = (...codes: (string | null)[]) =>
  codes.map((languageCode) => ({ languageCode }));

describe("pickLanguage", () => {
  it("picks the first preferred language the app has", () => {
    expect(pickLanguage(locales("de", "en", "fr"))).toBe("en");
  });

  it.each([
    ["no preferred language the app has", locales("de", "fr")],
    ["no language codes", locales(null)],
    ["no preferred languages", locales()],
    ["a code that is not a language", locales("toString")],
  ])("falls back to English for %s", (_, preferred) => {
    expect(pickLanguage(preferred)).toBe("en");
  });
});

describe("i18n", () => {
  it("is ready with the English text", () => {
    expect(i18n.isInitialized).toBe(true);
    expect(i18n.t("app.name")).toBe("Stockroom");
    expect(
      i18n.t("serverStatus.connecting", { serverUrl: "https://example.com" }),
    ).toBe("Connecting to https://example.com…");
  });
});
