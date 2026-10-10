// Expo's recommended rules, including React, hooks, imports, and TypeScript; formatting is Prettier's.
// https://docs.expo.dev/guides/using-eslint/
const { defineConfig } = require("eslint/config");
const expoConfig = require("eslint-config-expo/flat");

module.exports = defineConfig([
  expoConfig,
  {
    ignores: [".expo/*", "dist/*", "expo-env.d.ts"],
  },
]);
