// Expo's recommended rules, including React, hooks, imports, and TypeScript; formatting is Prettier's.
// https://docs.expo.dev/guides/using-eslint/
/* global __dirname -- Node defines it; Expo's config only declares the CommonJS globals */
const { defineConfig } = require("eslint/config");
const expoConfig = require("eslint-config-expo/flat");

module.exports = defineConfig([
  expoConfig,
  {
    ignores: [".expo/*", "dist/*", "expo-env.d.ts"],
  },
  {
    // Rules that need type information, which the TypeScript project service provides.
    files: ["**/*.ts", "**/*.tsx"],
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: __dirname,
      },
    },
    rules: {
      // A promise that is neither awaited nor handled hides its failure, for example an un-awaited
      // `render` in a test. Mark a deliberate fire-and-forget call with `void`.
      "@typescript-eslint/no-floating-promises": "error",
    },
  },
]);
