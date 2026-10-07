# Contributing to Stockroom

Thanks for your interest!

> [!TIP]
>
> The project is in its earliest stage, so the most useful contributions right now are feedback on the design.

## Start with the specification

[SPECIFICATION.md](SPECIFICATION.md) is the source of truth for scope, architecture, and decisions. If you want to change behaviour or add a feature:

1. Check whether it is already covered, or deliberately out of scope, in the specification.
2. Open an issue to discuss it before writing a large pull request.
3. If the change affects the design, update the specification and add an entry to its changelog in the same pull request.

## Tool versions

The repository uses pinned tool versions:

| Tool      | Version                               | Declared in                                           |
| --------- | ------------------------------------- | ----------------------------------------------------- |
| Node      | 24.19.x                               | `.nvmrc`, `engines.node` in `package.json`            |
| pnpm      | 12.9.1                                | `packageManager` and `engines.pnpm` in `package.json` |
| .NET SDK  | 10.0.401 or a later 10.0 feature band | `global.json`                                         |
| dotnet-ef | 10.0.12                               | `server/dotnet-tools.json`                            |

To set them up:

```sh
nvm use               # or any version manager that reads .nvmrc
corepack enable       # installs the pnpm version from packageManager
pnpm install
dotnet --version      # should print the SDK pinned in global.json
(cd server && dotnet tool restore)  # installs dotnet-ef for EF Core migrations
```

## Ground rules

- **Stock integrity comes first.** Quantities change only through ledger movements (see section 3.2 of the specification). Pull requests that write stock levels any other way will not be accepted.
- Keep pull requests small and focused on one change.
- Add or update tests with the behaviour they verify.
- Follow the existing code style; `.editorconfig` applies to all files. Run `pnpm format` before committing; CI checks it with `pnpm format:check`.
- Use [Conventional Commits](https://www.conventionalcommits.org/) for commit messages, for example `feat(stock): add void movement`.

## Licensing of contributions

Stockroom is licensed under the [AGPL-3.0](LICENSE.txt). By submitting a contribution, you agree that it is licensed under the same terms. Contributor sign-off (DCO or CLA) is still to be decided; see the open questions in the specification.

## Reporting bugs and vulnerabilities

- Bugs: open an issue with steps to reproduce, expected and actual behaviour, and your version.
- Security vulnerabilities: see [SECURITY.md](SECURITY.md).

> [!CAUTION]
>
> Do **not** open a public issue for a security vulnerability.
