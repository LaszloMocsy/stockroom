# Stockroom — Specification

|             |                                                         |
| ----------- | ------------------------------------------------------- |
| **Status**  | Draft v0.7                                              |
| **Date**    | 2026-10-09                                              |
| **License** | AGPL-3.0 (see [Decisions](#16-decisions-and-rationale)) |

> This document is versioned in itself. See the [Changelog](#19-changelog) at the bottom for what changed in each revision.

## 1. Overview

**Stockroom** is a self-hostable, open-source inventory tracker. It answers one question reliably: _"How many of each product do we have, and how did that number get there?"_

It is built for people who store many different products in a large storage space and need to:

1. Add and manage products (with IDs, SKUs, barcodes, etc.).
2. Track stock: **add**, **remove**, and **manually edit** counts.
3. See **statistics** about stock and movement.
4. (Nice-to-have) Have a full **audit trail** of every change.

### 1.1 Guiding principles

- **Simple first.** A non-technical warehouse user should be productive in minutes. Every core action (add 5, remove 3) is at most two taps from the home screen.
- **Phone first.** Day-to-day use happens on a phone in the warehouse, scanning barcodes with the camera. The web app complements the phone app; it does not replace it.
- **Trustworthy numbers.** Stock counts are derived from an append-only movement ledger, so a count is always explainable. Audit comes almost for free from this design.
- **One source of truth.** A single backend per organisation owns all data. Every client (mobile, web) is a thin client of its API.
- **Easy to self-host.** A backend container plus PostgreSQL, started with one `docker compose up`.
- **Own your data.** Full CSV/JSON export, plain PostgreSQL, no lock-in, no telemetry by default.

### 1.2 Scope of the first demo (MVP)

The first milestone is a demo for the first prospective user. It is deliberately small:

- **Mobile app (primary):** connect to a server, log in, scan or search a product, add stock, remove stock, set a counted quantity, create a product from an unknown barcode, see low-stock items.
- **Backend:** products, ledger-based stock, barcode lookup, ADMIN and STAFF roles, CSV export.
- **Minimal web app:** log in, read-only product and stock list with search, a product detail page with its movement history, and CSV download. Its purpose is to show that a web app exists and where it is going.

Target size: a **small-to-medium warehouse**. Quantities are **whole-number counts only**; weights, volumes, and pack conversions are out of scope for now.

### 1.3 Non-goals (for now)

- Accounting, invoicing, purchase orders, sales orders, or payments.
- Multi-tenancy inside one deployment. One deployment = one organisation. (A managed offering runs one separate instance per customer; see [Section 12.4](#124-managed-hosting-railway).)
- Weights, volumes, units of measure, pack conversions (post-MVP).
- Manufacturing / bill-of-materials.
- Hardware integrations beyond the phone camera and keyboard-wedge scanners.
- Offline operation (post-MVP, but the API is designed for it; see idempotency in [Section 10](#10-api)).

## 2. Users and Roles

Two roles only.

| Role      | Who                | Needs                                                          |
| --------- | ------------------ | -------------------------------------------------------------- |
| **ADMIN** | Owner / manager    | Full control: products, users, settings, stats, audit, export. |
| **STAFF** | Warehouse employee | Quickly add/remove stock, look up products, correct counts.    |

### 2.1 Permission matrix

| Action                                |         STAFF         | ADMIN |
| ------------------------------------- | :-------------------: | :---: |
| View products, stock, low-stock list  |          ✅           |  ✅   |
| Record stock movements (add/remove)   |          ✅           |  ✅   |
| Manual count adjustment               |          ✅           |  ✅   |
| Create products, edit product details |          ✅           |  ✅   |
| Archive / restore products            |          ❌           |  ✅   |
| View movement history of a product    |          ✅           |  ✅   |
| View full audit log                   |          ❌           |  ✅   |
| Void a movement                       |         ❌ ¹          |  ✅   |
| Manage users and settings             |          ❌           |  ✅   |
| Export data                           | ✅ (products & stock) |  ✅   |

¹ STAFF can void (undo) their own movements within the undo window, 5 minutes by default (section 12.2).

A viewer/read-only role is a possible later addition (see [Roadmap](#15-roadmap)).

## 3. Core Concepts and Domain Model

### 3.1 Entities

#### Identifiers

Every entity that appears in an API response carries two IDs:

- **`id` — internal, UUID v7.** Primary key and the target of foreign keys. Time-sortable, so inserts stay index-friendly. It never leaves the server: no API response, URL, or object-storage key contains it.
- **`public_id` — public, UUID v4.** Unique, immutable, and random. It is the only ID clients see, so it is used in routes, DTOs, and storage keys. It leaks nothing about creation order or volume. The API resolves it to the internal `id` at the edge.

Entities with a `public_id`: Product, Stock movement, User, and (P1) Location, Category, and Audit event. Entities never addressed through the API (Stock level, Refresh token, Settings, Product barcode) carry only the internal `id`, or a natural key where one exists.

Products also have **external identifiers** that people and scanners use: the `sku` (human-readable) and the `barcodes`. These are business data, not keys. They are not interchangeable with `id` or `public_id`.

#### Product

| Field                                    | Type                | Stage | Notes                                                                                 |
| ---------------------------------------- | ------------------- | :---: | ------------------------------------------------------------------------------------- |
| `id`                                     | UUID (v7)           |  MVP  | Internal primary key. Never exposed.                                                  |
| `public_id`                              | UUID (v4), unique   |  MVP  | Public ID used in the API.                                                            |
| `sku`                                    | string, unique      |  MVP  | Human-readable ID. Auto-generated if omitted (e.g. `SR-000123`).                      |
| `name`                                   | string, required    |  MVP  |                                                                                       |
| `description`                            | text, nullable      |  MVP  |                                                                                       |
| `barcodes`                               | child table         |  MVP  | One or more EAN/UPC/Code128/QR payloads per product; each unique across all products. |
| `min_stock`                              | integer, nullable   |  MVP  | Low-stock threshold.                                                                  |
| `archived_at`                            | timestamp, nullable |  MVP  | Soft delete.                                                                          |
| `created_at`, `updated_at`, `created_by` |                     |  MVP  |                                                                                       |
| `category_id`, `tags`                    |                     |  P1   |                                                                                       |
| `unit_cost`                              | decimal, nullable   |  P1   | Enables stock valuation.                                                              |
| `image`                                  | object storage key  |  P1   | See [Section 9.4](#94-object-storage).                                                |
| `unit`, `custom_fields`                  |                     |  P2   | Units of measure are deferred; every quantity is a whole count for now.               |

**Location** (P1) — a place in the storage (e.g. _Aisle 3 / Shelf B_), hierarchical. The schema includes a single built-in default location (`Main storage`) from day one so multi-location support is a non-breaking addition: `stock_levels` and `movements` already carry a `location_id`.

**Category** (P1) — hierarchical grouping of products.

**Stock level** — `(product_id, location_id) → quantity`. A **materialised cache** of the ledger, updated in the same transaction as each movement and verifiable/rebuildable from the ledger.

#### Stock movement (the ledger, append-only)

| Field                       | Type              | Notes                                                                                           |
| --------------------------- | ----------------- | ----------------------------------------------------------------------------------------------- |
| `id`                        | UUID (v7)         | Internal primary key. Time-sortable. Never exposed.                                             |
| `public_id`                 | UUID (v4), unique | Public ID used in the API.                                                                      |
| `product_id`, `location_id` | FK                |                                                                                                 |
| `type`                      | enum              | `receive`, `issue`, `adjust`, `initial`, `void`; later `transfer_out`/`transfer_in`.            |
| `delta`                     | integer           | Signed change applied to quantity.                                                              |
| `quantity_after`            | integer           | Resulting quantity at that location (makes audits and charts cheap).                            |
| `reason`                    | enum              | `purchase`, `sale`, `return`, `damaged`, `lost`, `found`, `correction`, `count`, `other`.       |
| `note`                      | text, nullable    | Free text.                                                                                      |
| `reference`                 | string, nullable  | External ref (order number, delivery note).                                                     |
| `voids_movement_id`         | UUID, nullable    | Set on `void` movements; points at the movement being reversed (the API shows its `public_id`). |
| `idempotency_key`           | string, nullable  | Client-supplied; unique per actor; prevents double-submits on flaky connections.                |
| `actor_id`                  | FK → user         |                                                                                                 |
| `created_at`                | timestamp         | Server-assigned, immutable.                                                                     |

**User** — `id`, `public_id`, `username`/`email`, `display_name`, `password_hash`, `role` (`ADMIN` | `STAFF`), `disabled_at`, `last_login_at`.

**Refresh token** — hashed at rest, per device, revocable. (Long-lived personal API tokens are P1.)

**Audit event** — see [Section 8](#8-audit-log).

### 3.2 Stock integrity rules (the important part)

1. **Quantities change only via movements.** There is no "UPDATE quantity". A manual edit is an `adjust` movement that records the delta needed to reach the entered target, with `reason` and optional note.
2. **Movements are immutable.** Mistakes are corrected with a new compensating movement, never by editing or deleting history. An ADMIN can _void_ a movement, which creates a linked reversing movement.
3. **Updates are atomic.** The ledger insert and stock-level update happen in a single PostgreSQL transaction. The stock-level row is locked (`SELECT … FOR UPDATE`) or updated with a guarded statement (`UPDATE … WHERE quantity >= @n`), so concurrent users cannot oversell or corrupt counts.
4. **Negative stock** is rejected by default (an `issue` greater than available returns an error). An ADMIN setting `allow_negative_stock` can relax this.
5. **Concurrency on manual edits:** a manual "set count to N" carries the `expected_current` value the user saw. If it has changed in the meantime, the server returns `409 Conflict` with the new value and the UI asks for confirmation.
6. **Reconciliation check:** a background job verifies `SUM(delta)` per product/location equals the cached stock level and reports drift.
7. **Whole numbers only (for now):** quantities are integers. Fractional input is rejected. Decimal quantities arrive together with units of measure.
8. **Idempotency:** a repeated request with the same `Idempotency-Key` from the same actor returns the original result and creates no new movement.

## 4. Functional Requirements

Priority: **MVP** = first demo, **P1** = v1.0, **P2** = nice-to-have / later.

### 4.1 Product management

- **MVP** Create, view, edit, archive products.
- **MVP** Unique SKU enforcement; auto-generate SKU.
- **MVP** Attach one or more barcodes to a product (scan to attach).
- **MVP** Search by name, SKU, barcode (as-you-type).
- **MVP** List with sort, filter (low-stock, archived), cursor pagination.
- **P1** Image upload with thumbnails.
- **P1** Categories and tags.
- **P1** Duplicate product (clone as template), bulk edit.
- **P1** Stock valuation via `unit_cost`.
- **P2** Variants, custom fields, units of measure.
- **P2** Printable labels (QR/Code128 with SKU + name).

### 4.2 Stock operations

- **MVP** **Add stock** (`receive`): product, quantity, optional reason/note.
- **MVP** **Remove stock** (`issue`): same, with insufficient-stock validation.
- **MVP** **Manual edit** (`adjust`): enter the _actual counted_ quantity; the system computes the delta; reason defaults to `count`.
- **MVP** Quick +1 / −1 buttons on the product screen, with a short undo window (implemented as a `void` movement).
- **MVP** Initial quantity on product creation recorded as an `initial` movement.
- **MVP** **Barcode scanning in the mobile app** (camera): scan to open a product; unknown barcode offers "create product" or "attach to existing product".
- **P1** Locations and **transfers** between them (atomic, two linked movements).
- **P1** **Batch session**: scan several products and apply quantities in one submit (e.g. receiving a delivery); all-or-nothing.
- **P1** **Count session**: choose a scope, enter counted quantities, review variance, commit as `adjust` movements.
- **P1** Barcode scanning in the web app (camera or keyboard-wedge scanner).
- **P2** Lots/batch numbers and expiry dates. Reservations.
- **P2** Offline queue on mobile (uses idempotency keys).

### 4.3 Alerts

- **MVP** Low-stock indicator in the apps when `quantity <= min_stock`; low-stock list.
- **P1** Optional email (SMTP) and webhook notifications.
- **P2** Slack/Discord/ntfy/push notifications.

### 4.4 Statistics and dashboard

See [Section 7](#7-statistics).

### 4.5 Audit

See [Section 8](#8-audit-log). The stock ledger gives a full audit trail for quantities in the MVP.

### 4.6 Import / Export

- **MVP** CSV export of products and current stock.
- **P1** CSV import for products (dry-run preview, row-level errors, upsert by SKU).
- **P1** CSV export of movements and audit log with date filters.
- **P1** Full JSON backup export / restore (ADMIN).

### 4.7 Users, auth, and settings

- **MVP** First-run setup creates the initial ADMIN account.
- **MVP** Username/password login with short-lived access tokens and refresh tokens.
- **MVP** ADMIN creates STAFF users and resets their passwords (minimal user admin). ADMIN can also change a user's display name and role, but there is always at least one ADMIN: the last one cannot be made STAFF.
- **P1** Disable users, personal API tokens, settings UI (instance name, SKU pattern, negative-stock policy, timezone, locale, reason list).
- **P2** OIDC / OAuth2 SSO, TOTP 2FA.

### 4.8 Localisation

- **P1** i18n framework from the start (all strings externalised) in both clients. English first; Hungarian is a likely second language.
- **P1** Locale-aware number/date formatting.

## 5. Key User Flows

1. **First launch (mobile):** enter the server URL (or scan the QR code shown in the web admin) → app checks compatibility → log in.
2. **Receive a delivery:** Home → _Add_ → scan product → enter quantity → Save. Toast confirms with _Undo_.
3. **Remove items:** Home → _Remove_ → scan → quantity → Save. Blocked with a clear message if insufficient.
4. **Unknown barcode:** scan → "No product found" → _Create product_ (barcode pre-filled) or _Attach to existing_ → optional initial quantity.
5. **Fix a wrong count:** Product → _Set count_ → enter actual number → Save. Previous value and delta shown for confirmation.
6. **Investigate a discrepancy:** Product → _History_ → see each movement with who/when/why (mobile and web).

## 6. Non-Functional Requirements

| Area                | Requirement                                                                                                                       |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| **Scale target**    | Up to ~10k products, ~1M movements, ~20 concurrent users on a small VPS or small managed container. List and search < 200 ms p95. |
| **Availability**    | Single backend instance; graceful shutdown; safe restarts at any time without data corruption.                                    |
| **Durability**      | PostgreSQL transactional writes; documented backup procedure; built-in backup command.                                            |
| **Security**        | See [Section 11](#11-security).                                                                                                   |
| **Accessibility**   | Labelled controls, sufficient contrast, large touch targets, no colour-only low-stock signalling.                                 |
| **Mobile**          | iOS and Android via Expo; usable one-handed; barcode scan to result in under 2 seconds on a mid-range phone.                      |
| **Connectivity**    | Requires a network connection in the MVP, but all stock-mutating requests are safe to retry.                                      |
| **Observability**   | Structured JSON logs, `/healthz` and `/readyz`, optional OpenTelemetry/Prometheus metrics.                                        |
| **Browser support** | Last 2 versions of Chrome, Safari, Firefox, Edge.                                                                                 |
| **Resource usage**  | Backend idle memory < 300 MB.                                                                                                     |

## 7. Statistics

All stats are computed from the movement ledger and stock levels.

### 7.1 Dashboard

- **MVP** Total products, total units on hand, low-stock and out-of-stock lists, recent activity feed.
- **P1** Stock value on hand (if `unit_cost` set), filters by date range/category/location.

### 7.2 Reports (P1)

- **Movement volume over time**: units in vs. out per day/week/month.
- **Stock level history** for a product (from `quantity_after`).
- **Top movers** and **dead stock** (no outgoing movement in N days).
- **Stock by category / location**.
- **Adjustment report**: manual corrections and net shrinkage over time.
- **Days of cover** estimate.

### 7.3 Export

- Every report exportable as CSV.

## 8. Audit Log

Two complementary layers:

1. **Stock ledger** (Section 3.1): immutable history of every quantity change, with actor, reason, note, and timestamp. **Included in the MVP.**
2. **Audit events** (P1): append-only log of everything else — product create/edit/archive (with before/after diffs), user and role changes, logins and failed logins, settings changes, imports/exports, voided movements.

Requirements:

- **P1** Every write action records actor, timestamp, action, entity type and public ID, and a JSON diff of changed fields, plus IP and user-agent for security-relevant events.
- **P1** Audit events and movements cannot be edited or deleted through the app or API.
- **P1** Searchable/filterable audit UI (ADMIN only) and CSV/JSON export.
- **P2** Tamper-evidence via a hash chain verified by a CLI command.
- **P2** Configurable retention for non-stock audit events (the ledger is never auto-purged).
- Deleting a product is a **soft delete**; hard purge is ADMIN-only, requires zero stock, and leaves a tombstone audit event.

## 9. Architecture

### 9.1 Stack

| Layer          | Choice                                                                                 | Rationale                                                                                     |
| -------------- | -------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Backend        | **.NET (current LTS)**, **ASP.NET Core** (minimal APIs or controllers)                 | Strong typing, mature transactions/concurrency, single small container, good OpenAPI support. |
| Database       | **PostgreSQL**                                                                         | Robust concurrent writes and row locking for the ledger; first-class on managed platforms.    |
| Data access    | **EF Core** with Npgsql; SQL migrations applied at startup                             | Typed queries and migrations; raw SQL where needed for stats and locking.                     |
| Auth           | **ASP.NET Core Identity** with bearer + refresh tokens                                 | Works the same for mobile and web; no cookies/CSRF for the apps.                              |
| API contract   | **OpenAPI 3.1** generated by the backend; **generated TypeScript client**              | The backend is the single contract; clients cannot drift from it.                             |
| Jobs           | `BackgroundService` / hosted services in the same process                              | Enough for reconciliation, digests, and backups. No external queue.                           |
| Web app        | **React + Vite** SPA, TanStack Query/Router, Tailwind + shadcn/ui                      | Authenticated admin app; no SSR needed. Builds to static files served by any web server.      |
| Mobile app     | **Expo (React Native)**, Expo Router, `expo-camera` for barcodes, `expo-secure-store`  | Native camera scanning; one TypeScript codebase for iOS and Android.                          |
| Object storage | **S3-compatible** via an abstraction, local disk as the self-host fallback             | See [Section 9.4](#94-object-storage).                                                        |
| Packaging      | Multi-arch Docker images (amd64/arm64): `stockroom-api`, `stockroom-web`               | Backend and web are separate images, so web can be hosted anywhere.                           |
| Testing        | xUnit + Testcontainers (PostgreSQL); Vitest; Playwright (web); Maestro (mobile, later) | Real PostgreSQL in tests, not mocks.                                                          |

Next.js was considered for the web app and rejected: this is an authenticated app with no SEO needs, and Next would add a Node server to run for no benefit.

### 9.2 High-level design

```txt
 Mobile app (Expo)  ─┐                      ┌──► PostgreSQL
 Web app (static)   ─┼──HTTPS──► Stockroom ─┤
 (future clients)   ─┘            API       └──► Object storage (S3-compatible)
                          (single container)       images, backups
```

- Each deployment is **one backend, one PostgreSQL database, one organisation**.
- Clients store only the **server URL** and their tokens. They never talk to the database or storage directly.
- **Modular monolith** inside the API: `products`, `stock` (ledger), `auth`, `audit`, `stats`, `importexport`, `storage`, later `locations` and `notifications`.
- The `stock` module is the only code allowed to write stock levels, which enforces the integrity rules in 3.2.

### 9.3 Repository layout

A monorepo with a .NET solution and a pnpm workspace.

```txt
stockroom/
├── server/                      # .NET solution
│   ├── Stockroom.Api/           # HTTP layer, auth, OpenAPI
│   ├── Stockroom.Core/          # domain + services (stock ledger rules)
│   ├── Stockroom.Data/          # EF Core, migrations
│   └── Stockroom.Tests/
├── apps/
│   ├── mobile/                  # Expo app
│   └── web/                     # Vite React app
├── packages/
│   └── api-client/              # generated from the OpenAPI document
├── docker/                      # Dockerfiles, compose examples
├── docs/                        # user + admin + API docs
├── SPECIFICATION.md
├── README.md
├── CONTRIBUTING.md
├── LICENSE                      # AGPL-3.0
└── CHANGELOG.md                 # product changelog (distinct from the spec changelog)
```

### 9.4 Object storage

Object storage holds **product images** (P1) and **database backups** (P1). The MVP needs none, so no object storage is required for the first demo.

The backend exposes an `IFileStorage` abstraction with two providers, selected by configuration:

| Provider | Use                                                                                     |
| -------- | --------------------------------------------------------------------------------------- |
| `local`  | Files on a mounted volume. Default for self-hosting; zero extra services.               |
| `s3`     | Any S3-compatible service: Railway Buckets, Cloudflare R2, AWS S3, MinIO, Backblaze B2. |

Rules that keep both providers interchangeable:

- Buckets are treated as **private**. The API either streams the object or hands out a **presigned URL**. Some providers (including Railway Buckets) do not offer public buckets at all.
- Stored objects are addressed by an opaque key (`products/{product_public_id}/{uuid}.webp`); the database stores only the key.
- Do not rely on versioning, object lock, server-side encryption, or lifecycle rules. Some providers lack them. Backup retention is implemented by the app's own job.
- Images are validated and re-encoded server-side, and thumbnails are generated at upload.
- Configuration uses the standard S3 variables: endpoint, region, bucket, access key ID, secret access key, path-style flag.

## 10. API

REST/JSON, versioned under `/api/v1`, described by an OpenAPI document at `/api/v1/openapi.json` (served in every environment, since clients are generated from it). An interactive API reference is served at `/api/docs` in the Development environment only. In every route and payload, `:id` and `*_id` fields are **public IDs** (UUID v4, see [3.1](#31-entities)); internal IDs are never exposed. Representative endpoints:

```txt
GET    /api/v1/info                        # version, min client version, setup state (public)
POST   /api/v1/setup                       # first-run: create initial ADMIN (only when no users exist)
POST   /api/v1/auth/login | /refresh | /logout
GET    /api/v1/me

GET    /api/v1/products?q=&low_stock=&archived=&cursor=
POST   /api/v1/products
GET    /api/v1/products/:id
PATCH  /api/v1/products/:id
POST   /api/v1/products/:id/archive | /restore       # ADMIN
POST   /api/v1/products/:id/barcodes
DELETE /api/v1/products/:id/barcodes/:barcode
GET    /api/v1/products/lookup?barcode=|sku=

POST   /api/v1/stock/movements             # receive | issue | adjust
GET    /api/v1/stock/movements?product=&type=&actor=&from=&to=&cursor=
POST   /api/v1/stock/movements/:id/void    # ADMIN, or the original actor within the undo window

GET    /api/v1/stats/summary
GET    /api/v1/export/products.csv

GET    /api/v1/users | POST | PATCH        # ADMIN
GET    /healthz   /readyz
```

Later: locations, categories, transfers, batches, audit, import, reports, API tokens, `/metrics`.

Conventions:

- Cursor-based pagination: a list responds with `{ items, next_cursor }`, where `next_cursor` is `null` on the last page. Lists that are not paged yet return everything in one page. Consistent error envelope `{ error: { code, message, details } }` with stable machine-readable `code` values.
- `Idempotency-Key` header supported on all stock-mutating endpoints.
- Rate limiting on auth endpoints.
- Quantities are JSON integers.

Example — remove 3 units:

```http
POST /api/v1/stock/movements
Idempotency-Key: 7c1b…
{
  "product_id": "…",
  "type": "issue",
  "quantity": 3,
  "reason": "sale",
  "note": "Order #1042"
}
```

Example — manual edit to counted value:

```http
POST /api/v1/stock/movements
{
  "product_id": "…",
  "type": "adjust",
  "target_quantity": 42,
  "expected_current": 45,
  "reason": "count"
}
```

### 10.1 Version compatibility

Every self-hosted server runs its own version, while the mobile app is a single binary in a store. The clients and server must therefore negotiate compatibility.

- `GET /api/v1/info` returns `{ server_version, api_version, min_client_version, setup_required }`.
- Each client sends `X-Client-Version` and `X-Client-Platform` headers.
- On connect, and after server or app updates, the app compares versions. It shows **"Please update the app"** if it is older than `min_client_version`, and **"This server is outdated, ask your administrator to update"** if the server is older than what the app requires.
- Within a major API version (`/api/v1`) changes are **additive only**: new fields and endpoints are fine; removing or changing the meaning of existing ones requires `/api/v2`.
- The previous major version is served in parallel for at least two minor releases after a new one ships.

### 10.2 Authentication

- Login returns a short-lived **access token** (about 15 minutes) and a **refresh token** (rotating, per device, revocable by ADMIN or on password reset).
- Access tokens are JWTs signed with HMAC-SHA256. They identify the user by public ID and carry the role. The server generates the signing key on first start and stores it in the database, so no secret has to be configured.
- Refresh tokens are random, opaque strings; the server stores only their SHA-256 hash. Each login starts a new device session, optionally labelled with a client-supplied `device_name` (e.g. "Anna's iPhone").
- `POST /api/v1/auth/refresh` uses up the presented refresh token and returns a new access and refresh token in the same session. Presenting a used refresh token again (a stolen copy or a replay) revokes the whole session, and its device has to log in again.
- A password reset by an ADMIN revokes all of the user's sessions and clears any login lockout.
- `POST /api/v1/auth/logout` takes the device's refresh token and revokes its session. Access tokens are not tracked, so one stays valid until it expires; clients discard it.
- Mobile stores tokens in `expo-secure-store`. Web keeps the access token in memory and the refresh token in an `httpOnly` cookie scoped to the auth endpoints, or in secure storage, to be decided at implementation time.
- Authorisation is enforced on the server for every request using the `ADMIN`/`STAFF` role claim. Clients hide controls for UX only.

## 11. Security

- Passwords hashed via the ASP.NET Core Identity hasher (PBKDF2); login rate limiting and lockout backoff. The auth endpoints accept 30 requests per minute per client address. After 5 wrong passwords in a row an account is locked for 1 minute, doubling with each further failure up to 15 minutes; a successful login resets the count. Both answer 429 with `Retry-After`.
- All inputs validated server-side; parameterised queries only (EF Core).
- Strict CORS: an explicit allowed-origins list, empty by default. Serving the web app behind the same reverse proxy as the API avoids CORS entirely and is the documented default.
- Secure headers (CSP for the web app, HSTS behind TLS, X-Content-Type-Options, frame-ancestors none).
- **HTTPS is required** for any non-local deployment. iOS blocks cleartext HTTP by default, and browsers require a secure context for camera access. The mobile app refuses `http://` servers except for `localhost` and explicit development builds.
- Uploads: type/size validated, images re-encoded, never served from an executable path.
- Secrets via environment variables or the platform's secret store; no secrets in images.
- The app trusts `X-Forwarded-*` headers only when configured.
- Dependency scanning and SBOM in CI; `SECURITY.md` with a private disclosure contact.
- No telemetry or outbound calls by default.

## 12. Deployment and Operations

### 12.1 Self-hosting

Docker Compose quick start (backend, PostgreSQL, optional web):

```yaml
services:
  db:
    image: postgres:17
    environment:
      POSTGRES_DB: stockroom
      POSTGRES_USER: stockroom
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes: ["pgdata:/var/lib/postgresql/data"]
    restart: unless-stopped

  api:
    image: ghcr.io/<org>/stockroom-api:latest
    depends_on: [db]
    ports: ["8080:8080"]
    environment:
      STOCKROOM_DATABASE_URL: Host=db;Database=stockroom;Username=stockroom;Password=${POSTGRES_PASSWORD}
      STOCKROOM_PUBLIC_URL: https://stock.example.com
      STOCKROOM_STORAGE_PROVIDER: local
    volumes: ["files:/data"]
    restart: unless-stopped

  web:
    image: ghcr.io/<org>/stockroom-web:latest
    depends_on: [api]
    ports: ["8081:80"]
    restart: unless-stopped

volumes:
  pgdata:
  files:
```

Documented guides: VPS + Caddy (automatic HTTPS), home server, and Railway.

### 12.2 Configuration

Environment variables with sane defaults, prefixed `STOCKROOM_`: database URL, public URL, allowed CORS origins, storage provider and S3 settings, SMTP, log level, undo window, reconciliation interval. Runtime-changeable preferences live in the database settings table (P1 admin UI). The web image reads its API base URL at container start, so one image works for every deployment.

### 12.3 Backup and upgrade

- **MVP:** documented `pg_dump` procedure.
- **P1:** `stockroom backup` command and scheduled backups to the configured file storage with retention managed by the app.
- Migrations run automatically at startup, are forward-only, and the app refuses to start against a database from a newer version.
- Semantic versioning; the product `CHANGELOG.md` calls out breaking changes.

### 12.4 Managed hosting (Railway)

A managed offering for non-technical customers is planned and is **not** a separate product: it is the same open-source images deployed per customer. Railway is the leading candidate platform.

#### Topology, one Railway project per customer

| Railway resource | Purpose                                                                        |
| ---------------- | ------------------------------------------------------------------------------ |
| `api` service    | The `stockroom-api` image. Public domain with automatic HTTPS.                 |
| `web` service    | The `stockroom-web` image (or static hosting). Optional per customer.          |
| PostgreSQL       | Railway's Postgres, connected over the private network.                        |
| Bucket           | Railway Bucket for images and backups (P1), connected via variable references. |

#### Railway Buckets (verified against the docs on 2026-10-05; re-check before committing)

- S3-compatible: object get/put/head/delete, list, copy, presigned URLs, tagging, multipart uploads.
- Pricing: about **$0.015 per GB-month**; S3 operations and bucket egress are free.
- **Private only.** There are no public buckets, so images are served by presigned URL or proxied through the API. The `IFileStorage` design already assumes this.
- **Not yet supported:** server-side encryption, object versioning, object locks, lifecycle configuration. App-level backup retention is therefore required.
- The **region is chosen at creation and cannot be changed**, so choose it deliberately (for example, near the customer).
- The free plan has limits (a 30-day trial allowance, then a small cap); a paid plan is needed for real customers.
- Services receive `BUCKET`, `ACCESS_KEY_ID`, `SECRET_ACCESS_KEY`, `ENDPOINT`, and `REGION` through variable references; these map directly onto the S3 settings in [Section 9.4](#94-object-storage).

#### Operational considerations for the managed offering

- Do not treat the platform's Postgres as the only copy of customer data: schedule off-platform database backups (for example, to a second provider's bucket) and test restores.
- Provide a template or script that stands up a customer project reproducibly, with per-customer secrets.
- **Account ownership (decided, D12):** the operator owns the Railway account and resells the managed service. For the first customer there is no billing system; costs are handled informally. Billing, pricing, and an SLA are deferred until there is a second customer. Because the operator holds the customers' data, the operator is responsible for backups, access control to the platform account, and a clear data-export path for each customer.
- Because the stack is plain containers, PostgreSQL, and S3-compatible storage, moving a customer to another provider is a database restore plus a bucket copy.

## 13. Quality and Testing

- **Unit tests** for the stock service: every rule in 3.2, including concurrency (parallel issues cannot oversell), idempotent retries, and conflict detection on manual edits.
- **Integration tests** run against real PostgreSQL (Testcontainers) in CI.
- **Property-based test:** after any random sequence of operations, cached stock levels equal the sum of the ledger.
- **API contract tests:** the generated OpenAPI document is checked in and diffed in CI so accidental breaking changes fail the build.
- **E2E:** Playwright for the web app; a small Maestro or Detox suite for the key mobile flows (post-MVP).
- **Migration tests:** upgrade from each previous release's fixture database.
- CI: lint, typecheck, tests, build multi-arch images, dependency audit.

## 14. Open Source Project Setup

- Licensed **AGPL-3.0**. Network use counts as distribution, so anyone running a modified Stockroom as a service must publish their changes. Contributions are accepted under the same license; consider a DCO sign-off.
- Repo files: `README` (screenshots/GIF), `LICENSE`, `CONTRIBUTING`, `CODE_OF_CONDUCT`, `SECURITY`, issue/PR templates, `CHANGELOG`.
- Seed/demo data script for development and screenshots.
- Docs site (for example VitePress or Starlight) from `/docs`.
- Conventional Commits; automated releases via GitHub Actions; images on GHCR.
- Mobile distribution: Expo Go or EAS internal builds for the demo; public App Store / Play Store release once the project has proven itself.

## 15. Roadmap

| Milestone            | Scope                                                                                                                                                                                                                   |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **M0 — Foundations** | Monorepo, tooling, CI, PostgreSQL schema + migrations, auth, OpenAPI → generated client, Docker images.                                                                                                                 |
| **M1 — MVP demo**    | Backend as in [1.2](#12-scope-of-the-first-demo-mvp); **mobile app** with scan/add/remove/set-count/create-product; **minimal web app** (read-only lists, product history, CSV). It is usable on a phone.               |
| **M2 — v1.0**        | Full web app (create/edit, user admin, stats and reports), locations and transfers, categories/tags, low-stock notifications, audit event log, CSV import, batch and count sessions, images, backups, i18n, API tokens. |
| **M3 — Polish**      | Managed-hosting template, offline queue on mobile, label printing, OIDC, 2FA, tamper-evident audit chain, store release.                                                                                                |
| **Later**            | Units of measure and decimals, variants, lots/expiry, reservations, read-only role, webhook ecosystem.                                                                                                                  |

## 16. Decisions and Rationale

| #   | Decision                                                    | Rationale                                                                                               |
| --- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| D1  | License: **AGPL-3.0**                                       | Keeps the project community-owned; a closed-source hosted fork would have to publish its changes.       |
| D2  | Target: small-to-medium warehouse, whole-number counts only | Keeps the MVP small; units and decimals can be added later without changing the ledger concept.         |
| D3  | Two roles: ADMIN and STAFF                                  | Sufficient for the first customer; fewer permission paths to test.                                      |
| D4  | Mobile-first (Expo), minimal web app in the MVP             | Daily use is on phones with barcode scanning; the web app shows the direction without doubling UI work. |
| D5  | .NET backend, single instance per organisation              | Single source of truth, simple operations, strong concurrency primitives.                               |
| D6  | PostgreSQL only                                             | Concurrent ledger writes and managed-platform support; avoids a two-database test matrix.               |
| D7  | Vite SPA rather than Next.js                                | No SSR/SEO need; static output is easiest to self-host.                                                 |
| D8  | OpenAPI-generated TypeScript client                         | Replaces shared Zod schemas across the .NET/TypeScript boundary; the backend is the contract.           |
| D9  | Bearer + refresh tokens instead of cookie sessions          | Works identically on mobile and web.                                                                    |
| D10 | Explicit version handshake between apps and server          | Self-hosted servers and store apps update independently.                                                |
| D11 | S3-compatible storage behind an abstraction, local fallback | Self-hosters need zero extra services; managed hosting can use Railway Buckets or any S3 provider.      |
| D12 | Managed hosting: operator-owned Railway account, resold     | The customer needs no infrastructure knowledge. No billing system until there is a second customer.     |

## 17. Open Questions

1. **Naming/branding:** confirm "Stockroom" is free of conflicts (trademark, package names, domain, GHCR org, app store names).
2. **Languages:** UI languages needed on day one (English, Hungarian)?
3. **Valuation:** is cost/value tracking needed, or counts only? (Currently P1.)
4. **Labels:** do the products already carry barcodes, or must Stockroom generate and print labels earlier than M3?
5. **Connectivity:** is Wi-Fi reliable throughout the storage space? (If not, the offline queue moves up the roadmap.)
6. **Managed hosting model:** account ownership is decided (operator-owned, D12). Still open: pricing, billing, and support commitments if the service is offered beyond the first customer.
7. **Contributor terms:** DCO sign-off versus a CLA under AGPL-3.0.
8. **Web token storage:** in-memory access token plus `httpOnly` refresh cookie versus secure storage (decide at implementation).

## 18. Glossary

- **SKU** — Stock Keeping Unit; the human-readable unique product ID.
- **Ledger** — the append-only table of stock movements.
- **Movement** — a single signed change to a product's quantity at a location.
- **Adjustment** — a movement that sets the count to an observed value, recorded as the delta.
- **Void** — a movement that reverses an earlier one; history is never deleted.
- **Low stock** — quantity at or below the product's `min_stock`.
- **Dead stock** — stock with no outgoing movement for a configured number of days.
- **Presigned URL** — a time-limited URL granting access to a private object in S3-compatible storage.

## 19. Changelog

Versions of this document. Newest first.

### v0.7 — 2026-10-09

- **Voids:** STAFF can void their own movements within the undo window, which defaults to 5 minutes and is set by an environment variable; ADMIN can void any movement at any time (sections 2.1, 12.2).
- **Reconciliation:** the check runs at startup and then on an interval set by an environment variable, hourly by default, and logs a warning per drifted level (sections 3.2, 12.2).

### v0.6 — 2026-10-08

- **Users:** lists respond with `{ items, next_cursor }` (section 10); the last ADMIN cannot be made STAFF (section 4.7); a password reset revokes all of the user's sessions and clears any login lockout (section 10.2).
- **Auth:** access tokens are JWTs signed with a key the server generates and stores in the database; refresh tokens are stored hashed and rotate on every refresh, reusing a used one revokes its device session, logout revokes the session by refresh token, and login takes an optional `device_name` (section 10.2). Concrete rate limit and lockout backoff for the auth endpoints (section 11).

### v0.5 — 2026-10-06

- **API docs:** the interactive reference at `/api/docs` is served in the Development environment only; the OpenAPI document at `/api/v1/openapi.json` stays available everywhere (section 10).

### v0.4 — 2026-10-06

- **Identifiers:** every API-visible entity has an internal UUID v7 `id` (never exposed) and a public UUID v4 `public_id` (used in routes, payloads, and storage keys). SKU and barcodes remain the external product identifiers. Added the "Identifiers" subsection to 3.1 and updated sections 8, 9.4, and 10.

### v0.3 — 2026-10-05

- **Managed hosting:** decided that the operator owns the Railway account and resells the service (D12). No billing system for the first customer; billing and pricing deferred.
- Updated section 12.4 and open question 6 accordingly.

### v0.2 — 2026-10-05

- **License:** decided AGPL-3.0.
- **Scope:** defined the first demo (MVP) as mobile-first with a minimal web app; targeted a small-to-medium warehouse.
- **Quantities:** whole-number counts only; units of measure and decimals deferred. Removed the `unit` field from the MVP model.
- **Roles:** reduced to ADMIN and STAFF; removed Viewer and rewrote the permission matrix.
- **Stack:** replaced the TypeScript/Node backend with .NET (ASP.NET Core, EF Core); PostgreSQL only (SQLite dropped); Vite + React web app; Expo mobile app; OpenAPI-generated TypeScript client.
- **Auth:** replaced cookie sessions with bearer + refresh tokens; added `/setup`, `/info`, and `/refresh` endpoints.
- **New sections:** version compatibility (10.1), authentication (10.2), object storage abstraction (9.4), managed hosting on Railway (12.4), decisions and rationale (16).
- **Architecture:** separate `api` and `web` images; monorepo layout with `server/`, `apps/`, `packages/api-client`.
- **Roadmap and priorities:** re-tagged requirements as MVP/P1/P2 and redefined milestones M0–M3.
- **Open questions:** removed those now answered (scale, units, users, devices, hosting DB, license, backend language); added managed-hosting and contributor-terms questions.
- Removed the PWA, SQLite, and Node/Hono/Drizzle references.

### v0.1 — 2026-10-05

- Initial draft: product overview, domain model with ledger-based stock, roles (Viewer/Staff/Admin), functional requirements, statistics, audit log, proposed TypeScript + SQLite/PostgreSQL stack, API sketch, security, deployment, roadmap, and open questions.
