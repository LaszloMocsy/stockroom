# Stockroom

![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue)
![Status: early development](https://img.shields.io/badge/status-early%20development-orange)

A self-hostable, open-source inventory tracker. Scan a barcode, add or remove stock, and always know how many of each product you have and how that number got there.

> [!IMPORTANT]
>
> Stockroom is in early development and nothing is released yet. The full design lives in [SPECIFICATION.md](SPECIFICATION.md).

## Table of contents

- [Stockroom](#stockroom)
  - [Table of contents](#table-of-contents)
  - [Highlights](#highlights)
  - [Planned stack](#planned-stack)
  - [Roadmap](#roadmap)
  - [Contributing](#contributing)
  - [Code of conduct](#code-of-conduct)
  - [Security](#security)
  - [License](#license)

## Highlights

- **Phone first.** A mobile app (Expo) for scanning barcodes and recording stock changes in the warehouse.
- **Trustworthy counts.** Stock is derived from an append-only movement ledger, so every number is explainable and auditable.
- **One source of truth.** A single backend per organisation; the mobile and web apps are thin clients of its API.
- **Easy to self-host.** Docker images for the API and web app, plus PostgreSQL.
- **Your data.** Plain PostgreSQL and CSV export. No lock-in, no telemetry.

## Planned stack

| Part     | Technology                                  |
| -------- | ------------------------------------------- |
| Backend  | .NET (ASP.NET Core, EF Core)                |
| Database | PostgreSQL                                  |
| Web      | React + Vite                                |
| Mobile   | Expo (React Native)                         |
| Contract | OpenAPI, with a generated TypeScript client |

## Roadmap

The first milestone is a mobile-first MVP with a minimal web app. See the [roadmap](SPECIFICATION.md#15-roadmap) in the specification for details.

- [ ] **M0 — Foundations:** monorepo, CI, database schema, auth, Docker images
- [ ] **M1 — MVP demo:** scan, add, remove and set counts on mobile; minimal read-only web app
- [ ] **M2 — v1.0:** full web app, locations and transfers, reports, audit log, CSV import
- [ ] **M3 — Polish:** managed-hosting template, offline queue, labels, SSO

## Documentation

- [Configuration](docs/configuration.md): the `STOCKROOM_*` environment variables.

## Contributing

Contributions are welcome once the project foundations land. Please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

## Code of conduct

Everyone taking part in the project is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Security

> [!IMPORTANT]
>
> Please report vulnerabilities privately, not in public issues. See [SECURITY.md](SECURITY.md).

## License

Stockroom is licensed under the [GNU Affero General Public License v3.0](LICENSE.txt).
