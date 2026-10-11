import "i18next";

import type { en } from "./locales/en";

// Types `t` from the English text: unknown keys and missing interpolation values fail the typecheck.
declare module "i18next" {
  interface CustomTypeOptions {
    defaultNS: "translation";
    resources: { translation: typeof en };
  }
}
