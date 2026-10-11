# Building the mobile app

This guide builds installable copies of the mobile app with [EAS Build](https://docs.expo.dev/build/introduction/), Expo's build service. For day-to-day development you need no build: run the app in Expo Go as [Run the mobile app](development.md#run-the-mobile-app) describes.

Commands below run from `apps/mobile` unless they say otherwise.

## Profiles

[`apps/mobile/eas.json`](../apps/mobile/eas.json) defines two build profiles:

| Profile       | What it builds                                                                                                                                                         | Platforms                    | Servers it connects to                    |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------- | ----------------------------------------- |
| `development` | A debug build with [`expo-dev-client`](https://docs.expo.dev/develop/development-builds/introduction/): your own Expo Go that loads the app from your Metro dev server | Android (APK), iOS Simulator | Any, including `http://` development APIs |
| `internal`    | A release build for testers, installed from a link ([internal distribution](https://docs.expo.dev/build/internal-distribution/))                                       | Android (APK)                | `https://` only, or `http://localhost`    |

The `internal` profile is a release build, so it refuses `http://` servers other than `localhost` (spec 11), and Android does not allow unencrypted traffic in it. Testers need a server reachable over HTTPS.

iOS internal distribution is not set up yet: it needs a paid Apple Developer account and each tester's device registered before the build.

## Prerequisites

- An [Expo account](https://expo.dev/signup), with access to the project the app is linked to (`extra.eas.projectId` and `owner` in [`app.json`](../apps/mobile/app.json)).
- The EAS command line, run with `npx eas-cli@latest` (or install it with `npm install --global eas-cli` and run `eas`).
- Nothing else for a cloud build: EAS builds on its own machines. A [local build](#build-on-your-own-machine) needs the Android SDK, or Xcode for iOS.

Sign in once:

```sh
npx eas-cli@latest login
npx eas-cli@latest whoami    # prints your Expo username
```

### Use your own Expo project

The committed `app.json` links the app to the maintainer's Expo project (`extra.eas.projectId` and `owner`). To build a fork under your own account, remove those two entries, then link the app to a new project of yours:

```sh
npx eas-cli@latest init   # offers to create @<your account>/stockroom, and writes its ID and your account to app.json
```

`eas init` also writes values that the config plugins already add (such as `ios.infoPlist`, `android.permissions`, and `extra.router`) into `app.json`. Keep only `extra.eas.projectId` and `owner`, and undo the rest, so the plugins stay the one place those are set.

Also change `ios.bundleIdentifier` and `android.package` in `app.json` (now `com.laszlomocsy.stockroom`): an identifier belongs to one developer once an app with it is published.

## Build for testers (internal)

```sh
npx eas-cli@latest build --profile internal --platform android
```

EAS uploads the repository, installs it with pnpm, builds the API client (the app's `eas-build-post-install` script), and builds a release APK; a build takes about 20 minutes, most of it Gradle. The first build asks to generate an Android keystore (with `--non-interactive`, it generates one without asking); let EAS generate and keep it. Every later build, and the store release, must be signed with that same key, or installed copies cannot be updated. To back it up, run `npx eas-cli@latest credentials --platform android` and download it.

When the build finishes, the command prints a link to its page on expo.dev, and a QR code. To install the app:

1. Open the link on the Android phone (or scan the QR code), and tap **Install**.
2. Allow your browser to install unknown apps when Android asks.
3. Open Stockroom and connect to your server's `https://` address.

Share the build page's link with testers; they need no Expo account to install. Builds are listed with `npx eas-cli@latest build:list`, and on the project's page on [expo.dev](https://expo.dev).

### Versions

The app's version (`version` in `app.json`) is what it sends to the server as `X-Client-Version` (spec 10.1); change it when releasing. The build number (Android's `versionCode`) is kept by EAS (`appVersionSource: remote`), and the `internal` profile raises it with each build, so a newer APK installs over an older one.

## Build for development

A development build is needed only for native code that Expo Go lacks; the app runs in Expo Go today. Build it once, and again whenever native dependencies or `app.json` change:

```sh
npx eas-cli@latest build --profile development --platform android   # APK for a phone or emulator
npx eas-cli@latest build --profile development --platform ios       # app for the iOS Simulator
```

Install it from the build's link, as above. For the iOS Simulator, `npx eas-cli@latest build:run --platform ios --latest` downloads the latest build and installs it.

Then start Metro for the development build rather than Expo Go, from the repository root:

```sh
pnpm --filter @stockroom/api-client build
pnpm --filter @stockroom/mobile start:dev-client
```

Open Stockroom on the device; it lists the dev servers it finds on your network, or scan the QR code that Metro prints. `pnpm mobile` still starts Metro for Expo Go (`expo start --go`).

## Build on your own machine

`--local` runs the same build on your computer instead of on EAS, which needs no build quota but does need the native tools: the Android SDK and a JDK for Android, or Xcode for iOS. It still needs an Expo login, for the project and its credentials:

```sh
npx eas-cli@latest build --profile internal --platform android --local
```

The APK is written to the current directory.

## Troubleshooting

- **The build cannot resolve `@stockroom/api-client`:** the API client was not built. Check that the build log ran `eas-build-post-install`, and that `server/openapi/openapi.v1.json` is committed: EAS uploads only files that git tracks.
- **"… is not encrypted. Use the server's https:// address." in an internal build:** release builds need `https://`. Use a development build for a development API.
- **The APK does not install over an older one:** it was signed with another key, for example after the project was re-created. Uninstall the old app first; its data on the phone (server address and sign-in) is lost.
