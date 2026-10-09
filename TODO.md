# Stockroom — Task Plan

Ordered, commit-sized tasks for building Stockroom up to the first demo (milestones **M0** and **M1** in [SPECIFICATION.md](SPECIFICATION.md#15-roadmap)). The AI assistant works through this file one task at a time.

## How to work from this file

1. **Pick the first unchecked task** (`- [ ]`) whose group dependencies are met. Groups are ordered; within a group, tasks are ordered.
2. **One task = one commit.** Do not mix tasks. Do not start the next task before the current one is committed.
3. **Read first.** Before coding, read the spec sections named in the group's `Spec:` line and any code the task touches. Make a short plan, then implement.
4. **Meet "Done when".** Each task has a verifiable outcome. Run the relevant build, lint, and tests and make sure they pass. Include tests with the behaviour they verify (in the same commit).
5. **Tick the box in the same commit.** Change `- [ ]` to `- [x]` for the finished task.
6. **Commit message:** [Conventional Commits](https://www.conventionalcommits.org/), with the task ID in the body, for example:

   ```txt
   feat(stock): add issue movement with insufficient-stock check

   Task: E2
   ```

7. **Too big or unclear?** If a task needs more than one commit, split it into smaller tasks in `TODO.md` first (its own commit), then continue. If the spec is ambiguous or a task conflicts with it, stop and ask. If the design changes, update `SPECIFICATION.md` and its changelog in the same commit.
8. **Do not push or open pull requests** unless the user asks.

### Non-negotiable rules

- **Stock quantities change only through `StockService` ledger movements** (spec 3.2). No other code writes `stock_levels` or edits/deletes movements.
- **Identifiers:** every API-visible entity has an internal UUID v7 `id` (primary and foreign keys only) and a unique public UUID v4 `public_id`. The API, routes, and storage keys use only `public_id`; DTOs never contain internal ids (spec 3.1).
- Quantities are **whole-number integers**. Server-side authorisation on every endpoint; clients only hide UI.
- Tests use **real PostgreSQL** (Testcontainers), not mocks of the database.
- API changes are **additive** within `/api/v1`; regenerate and commit the OpenAPI snapshot whenever the API changes. The API client's types are generated from the snapshot at build time and are not committed.
- No secrets in the repo. Configuration through `STOCKROOM_*` environment variables.
- Every new `STOCKROOM_*` variable gets a row in `docs/configuration.md` in the same commit that adds it.

### Task template

```md
- [ ] **X1** — Imperative title. _Done when:_ observable, checkable outcome.
```

## Group order

`A` → `B` → `C` → `D` → `E` → `F` → `G` → `H` → (`I` and `J` in either order, after `H`) → `K` → `L`

---

## A. Repository tooling

_Spec: 9.3, 14._

- [x] **A1** — Add root `package.json` and `pnpm-workspace.yaml` covering `apps/*` and `packages/*`. _Done when:_ `pnpm install` succeeds on the empty workspace.
- [x] **A2** — Pin Node and pnpm versions (`.nvmrc`, `packageManager`, `engines`). _Done when:_ versions are declared and documented in `CONTRIBUTING.md`.
- [x] **A3** — Add Prettier config and root `format` / `format:check` scripts. _Done when:_ `pnpm format:check` passes on the whole repo.
- [x] **A4** — Add .NET `global.json` and `server/Directory.Build.props` (nullable enabled, warnings as errors, latest analyzers, implicit usings). _Done when:_ file exists and `dotnet --version` resolves the pinned SDK.
- [x] **A5** — Add a GitHub Actions workflow skeleton that runs on push and pull request. _Done when:_ workflow runs and passes (it may only run `format:check` for now).
- [x] **A6** — Add a pull request template and issue templates (bug report, feature request). _Done when:_ templates are present under `.github/`.
- [x] **A7** — Add `CODE_OF_CONDUCT.md` (Contributor Covenant) and link it from `README.md`. _Done when:_ file exists and is linked.

## B. Backend foundation

_Spec: 6, 9.1–9.3, 10, 12.2._

- [x] **B1** — Create the .NET solution with `Stockroom.Core`, `Stockroom.Data`, `Stockroom.Api`, and `Stockroom.Tests` projects and project references. _Done when:_ `dotnet build` succeeds from `server/`.
- [x] **B2** — Make `Stockroom.Api` a minimal host that serves `GET /healthz` returning 200. _Done when:_ an integration test calls it with `WebApplicationFactory`.
- [x] **B3** — Add strongly typed, validated options bound from `STOCKROOM_*` environment variables (database URL, public URL, allowed CORS origins, log level). _Done when:_ startup fails with a clear message when required settings are missing; covered by a test.
- [x] **B4** — Add structured JSON console logging with request logging and a correlation ID. _Done when:_ requests log one JSON line each including the correlation ID.
- [x] **B5** — Add a global exception handler and the error envelope `{ error: { code, message, details } }`. _Done when:_ unhandled exceptions and validation failures return the envelope; covered by tests.
- [x] **B6** — Add `TimeProvider` and an ID generator abstraction in `Stockroom.Core` producing UUID v7 (internal ids) and UUID v4 (public ids). _Done when:_ all are injectable and unit tested, including v7 time-ordering and version bits for both.
- [x] **B7** — Generate the OpenAPI document and serve it at `/api/docs` (UI) and `/api/v1/openapi.json`. _Done when:_ the document is reachable and lists `/healthz`.
- [x] **B8** — Add `GET /api/v1/info` returning `{ server_version, api_version, min_client_version, setup_required }`. _Done when:_ endpoint is public, tested, and `setup_required` is a placeholder constant for now.
- [x] **B9** — Add the Testcontainers PostgreSQL fixture shared by integration tests. _Done when:_ a sample test starts a real PostgreSQL container and connects.

## C. Database and schema

_Spec: 3.1, 3.2._

- [x] **C1** — Add EF Core with Npgsql, `StockroomDbContext`, and a design-time factory. _Done when:_ `dotnet ef migrations list` runs.
- [x] **C2** — Apply migrations automatically at startup and refuse to start against a newer schema. _Done when:_ both behaviours are covered by tests.
- [x] **C3** — Add the `Product` entity and migration (id UUID v7, public_id UUID v4 unique, sku unique, name, description, min_stock, archived_at, created/updated timestamps, created_by). _Done when:_ migration applies and a repository test persists a product.
- [x] **C4** — Add `ProductBarcode` (barcode unique across all products, FK to product). _Done when:_ duplicate barcodes are rejected by a database constraint, tested.
- [x] **C5** — Add `Location` (id UUID v7, unique `public_id` UUID v4) and seed the default `Main storage` location in the migration. _Done when:_ the default location exists after migration with both ids.
- [x] **C6** — Add `StockLevel` (product, location, quantity; unique on the pair). _Done when:_ migration applies; uniqueness tested.
- [x] **C7** — Add `StockMovement` with all spec fields (including unique `public_id`), an index on `(product_id, created_at)`, and a unique index on `(actor_id, idempotency_key)`. _Done when:_ migration applies; the idempotency uniqueness is tested.
- [x] **C8** — Add a `Settings` table and typed accessor with `allow_negative_stock` (default false). _Done when:_ read/write covered by a test.
- [x] **C9** — Add a SKU sequence and generator producing `SR-000123` style values. _Done when:_ concurrent generation yields unique values, tested.

## D. Authentication and users

_Spec: 2, 4.7, 10.2, 11._

- [x] **D1** — Add ASP.NET Core Identity with EF stores (Guid keys, UUID v7 `id` plus a unique `public_id` UUID v4 on users) and the `ADMIN` and `STAFF` roles seeded. _Done when:_ migration applies, both roles exist, and users get both ids.
- [x] **D2** — Make `setup_required` in `/info` real (true when no users exist). _Done when:_ tested with and without users.
- [x] **D3** — Add `POST /api/v1/setup` creating the first ADMIN; reject once any user exists. _Done when:_ second call returns 409; tested.
- [x] **D4** — Add `POST /api/v1/auth/login` returning a short-lived access token and a refresh token. _Done when:_ valid and invalid credentials are tested.
- [x] **D5** — Store refresh tokens hashed, per device, and add `POST /api/v1/auth/refresh` with rotation and reuse detection. _Done when:_ reusing a rotated token revokes the chain; tested.
- [x] **D6** — Add `POST /api/v1/auth/logout` revoking the device's refresh token. _Done when:_ the revoked token can no longer refresh; tested.
- [x] **D7** — Add `GET /api/v1/me`. _Done when:_ returns `public_id` (as `id`), username, display name, and role for the authenticated user.
- [x] **D8** — Add authorisation policies (`RequireStaff`, `RequireAdmin`) and a convention that all endpoints require auth unless marked public. _Done when:_ a test fails any endpoint accidentally left anonymous.
- [x] **D9** — Add rate limiting and lockout backoff on auth endpoints. _Done when:_ repeated bad logins get 429 or lockout; tested.
- [x] **D10** — Add ADMIN endpoint `POST /api/v1/users` to create a STAFF (or ADMIN) user. _Done when:_ STAFF callers get 403; tested.
- [x] **D11** — Add ADMIN endpoints `GET /api/v1/users` and `PATCH /api/v1/users/:id` (`:id` is the public id; display name, role, password reset). _Done when:_ tested, including that the last ADMIN cannot be demoted.
- [x] **D12** — Add configurable CORS from allowed origins (empty by default). _Done when:_ allowed and disallowed origins are tested.

## E. Stock ledger (the core)

_Spec: 3.1, 3.2, 4.2, 10. Only `StockService` may write stock levels._

- [x] **E1** — Create `StockService` with `Receive` (adds stock, writes movement and updates level in one transaction). _Done when:_ tests cover new and existing stock level rows and `quantity_after`.
- [x] **E2** — Add `Issue` rejecting insufficient stock using a guarded update or row lock. _Done when:_ tests cover success and an `InsufficientStock` error.
- [x] **E3** — Honour the `allow_negative_stock` setting in `Issue`. _Done when:_ both setting values are tested.
- [x] **E4** — Add `Adjust` to a `target_quantity` computing the delta, with `expected_current` conflict detection. _Done when:_ tests cover success, no-op, and a stale `expected_current` returning a conflict.
- [x] **E5** — Record an `initial` movement through the service when a product is created with a starting quantity. _Done when:_ tested via the service API.
- [x] **E6** — Add `Void` creating a linked reversing movement; a movement can only be voided once. _Done when:_ tests cover success, double void, and voiding that would make stock negative.
- [x] **E7** — Add idempotency: a repeated key from the same actor returns the original result and writes nothing. _Done when:_ tested, including concurrent duplicates.
- [x] **E8** — Add a concurrency test: many parallel `Issue` calls never oversell. _Done when:_ test is deterministic and passes repeatedly.
- [x] **E9** — Add reconciliation: `SUM(delta)` per product/location versus the cached level, reporting drift. _Done when:_ a test that corrupts a level makes it report drift.
- [x] **E10** — Add a property-based test: random operation sequences keep levels equal to the ledger sum. _Done when:_ test runs in CI within a reasonable time.
- [x] **E11** — Add `POST /api/v1/stock/movements` (`receive`, `issue`, `adjust`) with validation and `Idempotency-Key` support. _Done when:_ endpoint tests cover each type and error codes.
- [x] **E12** — Add `GET /api/v1/stock/movements` with filters (product, type, actor, from, to) and cursor pagination. _Done when:_ pagination and filters are tested.
- [x] **E13** — Add `POST /api/v1/stock/movements/:id/void`, allowed for ADMIN or the original actor within the undo window (configurable, default 5 minutes). _Done when:_ permission cases are tested.
- [x] **E14** — Run reconciliation as a scheduled hosted service that logs drift. _Done when:_ the job runs on a configurable interval; tested with a short interval.

## F. Products API

_Spec: 3.1, 4.1, 4.6, 7.1, 10._

- [x] **F1** — Add `POST /api/v1/products` (name, optional SKU, description, `min_stock`, optional barcode, optional initial quantity). _Done when:_ SKU auto-generation, duplicate SKU/barcode errors, and the `initial` movement are tested.
- [x] **F2** — Add `GET /api/v1/products/:id` including current quantity. _Done when:_ tested, with 404 for unknown ids. `:id` is the public id; the internal id is absent from the response.
- [x] **F3** — Add `GET /api/v1/products` with cursor pagination and sorting. _Done when:_ tested with more than one page.
- [ ] **F4** — Add the `q` search (name, SKU, barcode) with a trigram index. _Done when:_ partial and case-insensitive matches are tested.
- [ ] **F5** — Add the `low_stock` and `archived` filters. _Done when:_ each filter is tested.
- [ ] **F6** — Add `PATCH /api/v1/products/:id` (name, description, `min_stock`; SKU immutable). _Done when:_ tested, including attempts to change the SKU.
- [ ] **F7** — Add ADMIN-only `archive` and `restore` endpoints. _Done when:_ STAFF get 403; archived products are hidden by default; tested.
- [ ] **F8** — Add `POST /api/v1/products/:id/barcodes` and `DELETE …/barcodes/:barcode`. _Done when:_ duplicate and last-barcode cases are tested.
- [ ] **F9** — Add `GET /api/v1/products/lookup?barcode=|sku=`. _Done when:_ found, not found, and archived cases are tested.
- [ ] **F10** — Add `GET /api/v1/stats/summary` (total products, total units, low-stock count, out-of-stock count, last 10 movements). _Done when:_ tested against seeded data.
- [ ] **F11** — Add `GET /api/v1/export/products.csv` (SKU, name, barcodes, quantity, min stock). _Done when:_ output is valid CSV with escaping, tested.

## G. Developer experience

_Spec: 14._

- [ ] **G1** — Add a dev seed command that creates an ADMIN, a STAFF user, and about 50 realistic products with movements. _Done when:_ running it twice is idempotent.
- [ ] **G2** — Add `docker/compose.dev.yml` running PostgreSQL for local development. _Done when:_ `docker compose up` provides a working database for the API.
- [ ] **G3** — Add a `docs/development.md` covering prerequisites, running the API, tests, and seeding. _Done when:_ a new contributor can follow it from a clean checkout.
- [ ] **G4** — Add .NET build and test to the CI workflow. _Done when:_ CI runs `dotnet build` and `dotnet test` with Testcontainers.

## H. API contract and generated client

_Spec: 9.1, 10.1, D8._

- [ ] **H1** — Write the OpenAPI document to `server/openapi/openapi.v1.json` and add a test that fails if the committed file is stale. _Done when:_ the test fails after an unregistered API change.
- [ ] **H2** — Create `packages/api-client` with type generation (openapi-typescript) from the committed snapshot at build time, and a typed fetch wrapper. Generated files are git-ignored. _Done when:_ `pnpm --filter api-client build` produces types from the snapshot and `git status` stays clean afterwards.
- [ ] **H3** — Add middleware to the client for the base URL, bearer token injection, and `X-Client-Version` / `X-Client-Platform` headers. _Done when:_ unit tested with a mock fetch.
- [ ] **H4** — Add automatic token refresh with a single in-flight refresh and a retry of the original request. _Done when:_ concurrent 401s trigger one refresh; unit tested.
- [ ] **H5** — Add a client helper `checkCompatibility(info, clientVersion)` returning `ok`, `app_outdated`, or `server_outdated`. _Done when:_ unit tested for all three outcomes.
- [ ] **H6** — Add CI steps that build `packages/api-client` from the committed snapshot, so a snapshot change that breaks the client's types fails CI. _Done when:_ CI is green on a clean tree and fails when the snapshot removes something the client uses.

## I. Mobile app (Expo)

_Spec: 1.2, 4.2, 5, 10.1, 11. Primary client for the MVP._

### I-a. Foundation

- [ ] **I1** — Scaffold `apps/mobile` with Expo, TypeScript, and Expo Router inside the workspace. _Done when:_ the app starts in the simulator and shows a placeholder screen.
- [ ] **I2** — Configure Metro for the pnpm monorepo and consume `packages/api-client`. _Done when:_ the app imports and calls a client function.
- [ ] **I3** — Add ESLint, TypeScript strict mode, and `typecheck` / `lint` scripts for the app. _Done when:_ both scripts pass and run in CI.
- [ ] **I4** — Add secure token storage using `expo-secure-store` behind a small interface. _Done when:_ unit tested with a fake store.
- [ ] **I5** — Add an app-wide data layer (TanStack Query) and an API client provider that reads the stored server URL. _Done when:_ a screen fetches `/info` through it.
- [ ] **I6** — Add an i18n scaffold with English strings. _Done when:_ visible text on existing screens comes from translation keys.

### I-b. Connect and sign in

- [ ] **I7** — Add the "Connect to server" screen: URL input, normalisation, and `/info` validation. _Done when:_ bad URLs and unreachable servers show clear errors.
- [ ] **I8** — Refuse `http://` servers except `localhost` and development builds. _Done when:_ unit tested.
- [ ] **I9** — Add the compatibility gate showing "update the app" or "server is outdated" using `checkCompatibility`. _Done when:_ both states are demonstrated with a mocked `/info`.
- [ ] **I10** — Add the first-run setup screen shown when `setup_required` is true (create the initial ADMIN). _Done when:_ completing it logs the user in.
- [ ] **I11** — Add the login screen. _Done when:_ successful login stores tokens; failure shows an error.
- [ ] **I12** — Add session handling: restore on launch, silent refresh, logout, and "change server". _Done when:_ app restart keeps the user signed in; logout clears tokens.
- [ ] **I13** — Add the app shell with tab navigation (Home, Products, Scan, Settings). _Done when:_ tabs render for signed-in users only.

### I-c. Browse

- [ ] **I14** — Add the Home screen with summary figures and a low-stock preview. _Done when:_ shows live data from `/stats/summary`.
- [ ] **I15** — Add the Products list with debounced search and infinite scroll. _Done when:_ typing filters results; scrolling loads more.
- [ ] **I16** — Add the product detail screen (name, SKU, barcodes, quantity, low-stock badge). _Done when:_ opens from the list.
- [ ] **I17** — Add the movement history list on the product detail screen. _Done when:_ shows who, when, type, delta, reason, and note, with infinite scroll.
- [ ] **I18** — Add the low-stock list screen. _Done when:_ reachable from Home and lists items at or below `min_stock`.

### I-d. Scan

- [ ] **I19** — Add a reusable barcode scanner component using `expo-camera` with a permission flow. _Done when:_ denied and granted states are handled; scans emit a barcode string once per detection.
- [ ] **I20** — Wire Scan → product lookup → open the product detail. _Done when:_ scanning a known barcode opens the product.
- [ ] **I21** — Handle unknown barcodes with a sheet offering "Create product" or "Attach to existing". _Done when:_ both options navigate correctly with the barcode carried along.

### I-e. Stock actions

- [ ] **I22** — Add a mutation helper that generates an idempotency key per user action and reuses it on retry. _Done when:_ unit tested.
- [ ] **I23** — Add the "Add stock" sheet (quantity stepper, optional reason and note). _Done when:_ saving updates the product quantity.
- [ ] **I24** — Add the "Remove stock" sheet with a clear insufficient-stock error. _Done when:_ removing more than available shows the message and changes nothing.
- [ ] **I25** — Add the "Set count" flow showing previous value and delta, and handling a conflict by displaying the new value for confirmation. _Done when:_ a simulated conflict is handled.
- [ ] **I26** — Add +1 / −1 quick buttons with a toast and an undo action that voids the movement. _Done when:_ undo restores the quantity within the undo window.
- [ ] **I27** — Add the "Create product" form (name, optional SKU, `min_stock`, barcode, initial quantity). _Done when:_ creating from an unknown scan lands on the new product.
- [ ] **I28** — Add "Attach barcode to existing product" (search, pick, confirm). _Done when:_ the barcode resolves to that product on the next scan.
- [ ] **I29** — Add a product edit form (name, description, `min_stock`). _Done when:_ changes persist and show on the detail screen.
- [ ] **I30** — Add ADMIN-only archive / restore actions on the product detail. _Done when:_ hidden for STAFF; works for ADMIN.

### I-f. Quality and delivery

- [ ] **I31** — Add offline/failed-request handling: a visible banner and safe retry for stock mutations. _Done when:_ a dropped request can be retried without double-counting.
- [ ] **I32** — Add `eas.json` with a development and an internal-distribution profile and document the steps in `docs/mobile.md`. _Done when:_ an internal build can be produced following the doc.
- [ ] **I33** — Add app icons and splash screen, and set the app name and bundle identifiers. _Done when:_ assets appear in a build.

## J. Web app (minimal)

_Spec: 1.2, 4.7, 9.1. Scope is intentionally small: read-only lists plus basic user admin._

- [ ] **J1** — Scaffold `apps/web` with Vite, React, TypeScript, and Tailwind. _Done when:_ `pnpm --filter web dev` serves a placeholder page.
- [ ] **J2** — Initialise shadcn/ui and add the base components used (button, input, table, card, dialog). _Done when:_ components render on a demo page.
- [ ] **J3** — Add runtime configuration: the API base URL is read from `/config.js` generated at container start. _Done when:_ the app uses the injected URL and falls back to a dev default.
- [ ] **J4** — Add TanStack Router, TanStack Query, and the API client provider. _Done when:_ a route fetches `/info`.
- [ ] **J5** — Add the first-run setup page. _Done when:_ shown only when `setup_required` is true.
- [ ] **J6** — Add the login page and auth state (access token in memory, refresh handling). _Done when:_ login, refresh, and logout work; protected routes redirect.
- [ ] **J7** — Add the app layout with navigation and a user menu. _Done when:_ navigation shows only for signed-in users.
- [ ] **J8** — Add the dashboard page (summary figures, low-stock list, recent activity). _Done when:_ shows live data.
- [ ] **J9** — Add a read-only products table with search and pagination. _Done when:_ search and paging work against the API.
- [ ] **J10** — Add the product detail page with stock and movement history. _Done when:_ linked from the table.
- [ ] **J11** — Add a "Download CSV" button for the product export. _Done when:_ downloads the file with the auth token.
- [ ] **J12** — Add the ADMIN-only users page (list, create STAFF, reset password). _Done when:_ route is hidden and blocked for STAFF.
- [ ] **J13** — Add a Playwright smoke test (setup → login → see products). _Done when:_ runs in CI against a seeded stack.

## K. Packaging and delivery

_Spec: 12._

- [ ] **K1** — Add a multi-stage `Dockerfile` for the API (non-root user, health check). _Done when:_ the image builds and `/healthz` responds in a container.
- [ ] **K2** — Add a `Dockerfile` for the web app (nginx serving static files and generating `config.js` from environment). _Done when:_ the container serves the app with a configurable API URL.
- [ ] **K3** — Add `docker/compose.yml` (PostgreSQL, API, web) with `.env.example`. _Done when:_ `docker compose up` yields a working stack.
- [ ] **K4** — Add a Caddy example for HTTPS and same-origin routing of web and API. _Done when:_ documented and tested locally.
- [ ] **K5** — Add a CI workflow that builds multi-arch images and publishes them to GHCR on version tags. _Done when:_ a test tag produces both images.
- [ ] **K6** — Add `docs/self-hosting.md` with setup, upgrades, and `pg_dump` / restore instructions. _Done when:_ following it on a clean machine works.
- [ ] **K7** — Add release notes workflow and cut `v0.1.0` (update `CHANGELOG.md`). _Done when:_ the tag and a GitHub release exist.

## L. Documentation polish

- [ ] **L1** — Add a README quick start (self-host in a few commands) and mobile install notes. _Done when:_ instructions match what K3 and I32 deliver.
- [ ] **L2** — Add `docs/api.md` summarising authentication, idempotency, errors, and the version handshake, linking the OpenAPI document. _Done when:_ consistent with the implemented API.
- [ ] **L3** — Add screenshots or a short demo recording to the README. _Done when:_ images are committed and referenced.
- [ ] **L4** — Review `SPECIFICATION.md` against what was built and record deviations in its changelog. _Done when:_ spec and implementation agree, and a new changelog entry exists.

---

## Backlog (after the first demo)

Not yet broken into commits. Split each into tasks in this file when it becomes next. See the roadmap in the specification.

- **M2 / v1.0:** locations and transfers; categories and tags; batch and count sessions; images and object storage (`IFileStorage`, local and S3 providers); audit event log and UI; low-stock notifications (email, webhook); full web app (create/edit, reports, stats charts); CSV import; API tokens; backup command and schedule; i18n (Hungarian); settings UI.
- **M3 / polish:** managed-hosting template for Railway; offline queue on mobile; label printing; OIDC; TOTP 2FA; tamper-evident audit chain; store release.
- **Later:** units of measure and decimals, variants, lots and expiry, reservations, a read-only role.
