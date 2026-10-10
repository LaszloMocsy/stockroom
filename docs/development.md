# Development

How to run Stockroom from a clean checkout: the API against a local database, the mobile app, the tests, and the development seed data. For contribution rules, see [CONTRIBUTING.md](../CONTRIBUTING.md); for every setting, see [Configuration](configuration.md).

## Contents

- [Prerequisites](#prerequisites)
- [First-time setup](#first-time-setup)
- [Start the database](#start-the-database)
- [Run the API](#run-the-api)
- [Seed development data](#seed-development-data)
- [Run the mobile app](#run-the-mobile-app)
- [Run the tests](#run-the-tests)
- [Change the database schema](#change-the-database-schema)
- [Change the API](#change-the-api)
- [Before you commit](#before-you-commit)
- [Troubleshooting](#troubleshooting)

## Prerequisites

| Tool                                                            | Version                               | Used for                                                            |
| --------------------------------------------------------------- | ------------------------------------- | ------------------------------------------------------------------- |
| [.NET SDK](https://dotnet.microsoft.com)                        | 10.0.401 or a later 10.0 feature band | Building, running, and testing the API                              |
| Docker (Desktop, Engine, or OrbStack)                           | Docker Compose v2                     | The development database and the integration tests                  |
| Node                                                            | 24.19.x                               | Repository tooling (formatting), the API client, and the mobile app |
| pnpm                                                            | 12.9.1, via Corepack                  | The same                                                            |
| Xcode with an iOS Simulator, or Android Studio with an emulator | Current stable                        | Running the mobile app; optional, a phone with Expo Go also works   |

The exact versions are pinned in the repository; see [Tool versions](../CONTRIBUTING.md#tool-versions). Commands below run from the repository root unless they start with `cd`.

## First-time setup

```sh
nvm use                             # or any version manager that reads .nvmrc
corepack enable                     # installs the pnpm version from package.json
pnpm install                        # repository tooling (Prettier), the API client, and the mobile app
dotnet --version                    # should print the SDK pinned in global.json
(cd server && dotnet tool restore)  # dotnet-ef, for migrations
(cd server && dotnet build)
```

The build treats warnings, including code-style violations, as errors, so a clean build means the code is ready for review.

## Start the database

[`docker/compose.dev.yml`](../docker/compose.dev.yml) runs PostgreSQL 17 on `localhost:5432` with database, user, and password all set to `stockroom`. These are the defaults the API uses in development, so nothing needs configuring.

```sh
docker compose -f docker/compose.dev.yml up -d --wait   # start; returns once the database accepts connections
docker compose -f docker/compose.dev.yml stop           # stop, keeping the data
docker compose -f docker/compose.dev.yml down -v        # remove, including the data, for a fresh database
```

The data lives in a Docker volume, so it survives restarts until you run `down -v`.

## Run the API

```sh
cd server
dotnet run --project Stockroom.Api
```

The API listens on <http://localhost:5278> and applies database migrations at startup. Check that it is up:

```sh
curl http://localhost:5278/healthz           # 200, empty body
curl http://localhost:5278/api/v1/info       # server and API versions, and setup_required
```

- **API reference:** <http://localhost:5278/api/docs> (development only) lets you browse and call every endpoint.
- **OpenAPI document:** <http://localhost:5278/api/v1/openapi.json>.
- **Configuration:** `dotnet run` uses the `Development` environment, where [`appsettings.Development.json`](../server/Stockroom.Api/appsettings.Development.json) supplies the database URL and public URL. Environment variables override it, for example `STOCKROOM_LOG_LEVEL=Debug dotnet run --project Stockroom.Api`. See [Configuration](configuration.md).
- **Logs** are JSON, one object per line, on standard output.
- **HTTPS:** `dotnet run --project Stockroom.Api --launch-profile https` also listens on <https://localhost:7139>. Run `dotnet dev-certs https --trust` once first.

Stop it with <kbd>Ctrl</kbd>+<kbd>C</kbd>.

### Signing in

A new database has no users, and `/api/v1/info` reports `"setup_required": true`. Either [seed it](#seed-development-data), or create the first ADMIN yourself:

```sh
curl -X POST http://localhost:5278/api/v1/setup \
  -H 'Content-Type: application/json' \
  -d '{"username": "me", "display_name": "Me", "password": "at least 8 characters"}'
```

Then log in to get an access token and send it as a bearer token:

```sh
curl -X POST http://localhost:5278/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username": "admin", "password": "stockroom-admin"}'

curl http://localhost:5278/api/v1/products -H 'Authorization: Bearer <access_token>'
```

In the API reference, paste the access token under _Authentication_ to try authenticated endpoints.

## Seed development data

```sh
cd server
dotnet run --project Stockroom.Api -- seed
```

The `seed` command migrates the database, fills it, prints what it created, and exits; it does not start the server. It creates:

- an ADMIN user, `admin` with password `stockroom-admin`;
- a STAFF user, `staff` with password `stockroom-staff`;
- about 50 office and workshop products with SKUs, barcodes (valid EAN-13 codes), and low-stock thresholds;
- about three months of stock history for them (deliveries, removals, counts, and voids), with a few products out of stock or low.

It is safe to run again. Users are matched by username and products by SKU, and only missing ones are created, so a second run creates nothing and leaves your own changes alone. For a fresh copy of the seed data, reset the database with `docker compose -f docker/compose.dev.yml down -v`, start it again, and seed.

The command only runs in the `Development` environment, because the seed users have well-known passwords. `dotnet run` uses `Development`; anywhere else, it refuses and exits with code 1.

## Run the mobile app

The mobile app lives in [`apps/mobile`](../apps/mobile). It uses [Expo](https://docs.expo.dev) (SDK 57) with [Expo Router](https://docs.expo.dev/router/introduction/): every file under `apps/mobile/src/app` is a screen. It talks to the API through [`packages/api-client`](../packages/api-client), so [start the API](#run-the-api) first.

```sh
pnpm mobile                     # build the API client, then start the Metro dev server
```

With the dev server running, press <kbd>i</kbd> to open the app in the iOS Simulator or <kbd>a</kbd> for the Android emulator; the first time, Expo installs [Expo Go](https://expo.dev/go) on it. To use a phone instead, install Expo Go from the App Store or Google Play and scan the QR code that the dev server prints; the phone must be on the same network as your computer. Saving a file reloads the app.

`pnpm mobile --ios` and `pnpm mobile --android` start the dev server and open the simulator or emulator in one step. Other options go to `expo start` the same way.

The app imports the API client's built output, not its sources, so `pnpm mobile` builds the client first. After changing the client or the OpenAPI snapshot, rebuild it with `pnpm --filter api-client build`; the running dev server picks up the change. Expo configures Metro for the pnpm workspace by itself, so the app has no `metro.config.js`.

The app talks to the server whose URL it has stored. Until it stores one, it opens the Connect screen, which checks the address with `/api/v1/info` before storing it. The app also compares its version with the server's there (spec 10.1), and again whenever it returns to the foreground; while they cannot work together, it shows an "Update needed" screen instead. Raise `min_client_version` on the server or `RequiredApiVersion` in `packages/api-client` to see it. A server without users (`setup_required`) opens the setup screen, which creates the first ADMIN and signs in with it; reset the database to see it again. The screen suggests `EXPO_PUBLIC_API_URL`, or in development `http://localhost:5278`, which works in the iOS Simulator. Elsewhere, set `EXPO_PUBLIC_API_URL` when starting the dev server, for example `EXPO_PUBLIC_API_URL=http://10.0.2.2:5278 pnpm mobile` for the Android emulator. A phone needs your computer's network address, and the API has to listen on it rather than only on `localhost`. Once connected, the app asks you to sign in, for example as the [seeded](#seed-development-data) `admin`; after that, the placeholder screen shows whether it reaches the API. The app does not keep you signed in across restarts yet.

Only development builds connect to `http://` servers anywhere; release builds accept `http://` only for `localhost` and otherwise need `https://` (spec 11). Likewise, Android allows unencrypted traffic only in the debug variants that development builds use: the app sets `usesCleartextTraffic` to `false` with `expo-build-properties`, and Expo's template turns it back on in the debug manifests. `native-config.test.ts` checks the release setting.

All text the app shows comes from translation keys ([i18next](https://www.i18next.com) with `react-i18next`): use `const { t } = useTranslation()` and `t("home.comingSoon")` rather than writing text in components. English, the source language, is in [`apps/mobile/src/i18n/locales/en.ts`](../apps/mobile/src/i18n/locales/en.ts). Keys and interpolation values are typed from it, so a misspelt key fails the typecheck. The app uses the device's language when it has it, and English otherwise; [`src/i18n/index.ts`](../apps/mobile/src/i18n/index.ts) explains how to add a language.

Expo writes generated files to `apps/mobile/.expo/` and `apps/mobile/expo-env.d.ts`, including the types for typed routes. They are git-ignored; delete them if they get stale, or start with `pnpm mobile --clear`, which also clears Metro's cache.

## Run the tests

```sh
cd server
dotnet test
```

The tests use [xUnit v3](https://xunit.net) on Microsoft Testing Platform. Integration tests run against a real PostgreSQL server that [Testcontainers](https://dotnet.testcontainers.org) starts in Docker, so Docker must be running. They do not use the development database: each test gets its own empty database in a throwaway container, which is removed when the run ends. The first run pulls the `postgres:17` image, so it takes longer.

To run part of the suite, pass filters after `--`:

```sh
dotnet test --project Stockroom.Tests -- --filter-namespace 'Stockroom.Tests.Core'       # unit tests; no Docker needed
dotnet test --project Stockroom.Tests -- --filter-class 'Stockroom.Tests.Api.DevSeedTests'
dotnet test --project Stockroom.Tests -- --filter-method '*Idempotent*'
```

Tests are organised by layer: `Core` (pure unit tests), `Data` (EF Core and the stock ledger against PostgreSQL), and `Api` (HTTP endpoints, hosted in memory with `WebApplicationFactory`, and a few that start the API as a separate process).

The TypeScript API client has its own unit tests, run with [Vitest](https://vitest.dev) against a mock `fetch`, so they need neither Docker nor a running API:

```sh
pnpm --filter api-client test
pnpm --filter api-client typecheck   # also type-checks the tests against the generated types
```

The mobile app's unit tests run with [Jest](https://jestjs.io) and the [`jest-expo`](https://docs.expo.dev/develop/unit-testing/) preset, which mocks Expo's native modules as on iOS. They import the built API client, so build it first:

```sh
pnpm --filter api-client build
pnpm --filter mobile test
```

Tests sit next to the code they test, as `*.test.ts` or `*.test.tsx`, and import `describe`, `it`, `expect`, and `jest` from `@jest/globals`. Keep them out of `apps/mobile/src/app`, where Expo Router would treat them as screens. Components are tested with [React Native Testing Library](https://callstack.github.io/react-native-testing-library/), whose `render` is asynchronous: `await render(...)`. To test a component that calls the API, wrap it in `ApiProvider` with a `MemoryStore`-backed storage and stub `globalThis.fetch`, as in `src/components/server-status.test.tsx`.

## Change the database schema

Migrations live in `server/Stockroom.Data/Migrations` and are applied automatically when the API starts. After changing an entity or its configuration:

```sh
cd server
dotnet ef migrations add <Name> --project Stockroom.Data
```

Adding a migration does not need a database. Commit the generated files, including the updated model snapshot, together with the change. Migrations are forward-only once released: a server refuses to start against a database migrated by a newer version.

## Change the API

The API contract is committed as [`server/openapi/openapi.v1.json`](../server/openapi/openapi.v1.json), and the TypeScript client is generated from it. `OpenApiSnapshotTests` fails whenever the document the API serves differs from that file, so every change to an endpoint, request, or response has to update it. After an intended change, regenerate it:

```sh
cd server
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project Stockroom.Tests -- --filter-class '*OpenApiSnapshotTests'
```

Review the diff and commit the snapshot together with the change. Then rebuild the TypeScript client, [`packages/api-client`](../packages/api-client), which generates its types from the snapshot at build time; the generated files are not committed:

```sh
pnpm --filter api-client build
```

CI does the same on every pull request: it fails if the snapshot is stale, and it fails if the snapshot drops or changes something the client uses, because the client then no longer compiles.

Within `/api/v1`, changes must be additive: new endpoints and fields are fine, but removing or changing existing ones needs `/api/v2` (see the [specification](../SPECIFICATION.md#101-version-compatibility)).

## Before you commit

```sh
pnpm format:check              # Prettier, as in CI; `pnpm format` fixes it
(cd server && dotnet build)    # warnings and code-style violations fail the build
(cd server && dotnet test)
pnpm --filter api-client typecheck
pnpm --filter api-client test
pnpm --filter api-client build  # the app's checks need the built client
pnpm --filter mobile typecheck  # TypeScript, strict
pnpm --filter mobile lint       # ESLint with Expo's rules; warnings fail too
pnpm --filter mobile test
```

The app's typecheck uses the typed routes that `expo start` generates in `apps/mobile/.expo/types`, when they exist. Without them, for example on a fresh clone, it still passes, but route paths are not checked.

## Troubleshooting

- **Port 5432 is already in use:** another PostgreSQL is running locally. Stop it, or change the published port in `docker/compose.dev.yml` (for example `"127.0.0.1:5433:5432"`) and point the API at it, for example `STOCKROOM_DATABASE_URL='Host=localhost;Port=5433;Database=stockroom;Username=stockroom;Password=stockroom'`.
- **The API exits with `Failed to connect to 127.0.0.1:5432`:** the database is not running. Start it with `docker compose -f docker/compose.dev.yml up -d --wait`.
- **The API refuses to start because the database is newer:** the database was migrated by a newer version, for example on another branch. Switch back, or reset the database with `down -v`.
- **The app shows `Unable to resolve module` for a package that is installed:** Metro's cache still points at files from before a `pnpm install`. Start the dev server with `pnpm mobile --clear`.
- **Integration tests fail with a Docker error:** Docker is not running, or the current user cannot reach it. `docker info` should succeed.
- **Login answers 429:** either more than 30 auth requests a minute came from your address, or the account is locked after 5 wrong passwords in a row (`account_locked_out`; 1 minute at first, doubling up to 15). Wait and try again. An ADMIN resetting the password clears a lockout; on a seeded database, so does resetting the database.
