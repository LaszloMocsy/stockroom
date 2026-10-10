import { getLocales } from "expo-localization";
import { createInstance } from "i18next";
import { initReactI18next } from "react-i18next";

import { en } from "./locales/en";

/**
 * The app's languages (spec 4.8). To add one, add a file under `locales` typed as `Translations`, list
 * it here, and add its code to `supportedLocales` for expo-localization in `app.json`.
 *
 * Hermes on iOS has no `Intl.PluralRules`, so i18next falls back to `_one` for a count of 1 and `_other`
 * otherwise. That is right for English and Hungarian; a language with more plural forms needs a
 * polyfill such as `@formatjs/intl-pluralrules`.
 */
export const resources = {
  en: { translation: en },
} as const;

export type Language = keyof typeof resources;

export const defaultLanguage: Language = "en";

/** The first of the user's preferred languages that the app has, or English. */
export function pickLanguage(
  locales: readonly { languageCode: string | null }[],
): Language {
  for (const { languageCode } of locales) {
    if (languageCode && Object.hasOwn(resources, languageCode)) {
      return languageCode as Language;
    }
  }
  return defaultLanguage;
}

const i18n = createInstance();

// Resources are bundled, so initialising synchronously makes text available on the first render.
// initReactI18next makes this instance the one that useTranslation uses.
void i18n.use(initReactI18next).init({
  resources,
  lng: pickLanguage(getLocales()),
  fallbackLng: defaultLanguage,
  initAsync: false,
  // React escapes rendered text already.
  interpolation: { escapeValue: false },
});

export default i18n;
