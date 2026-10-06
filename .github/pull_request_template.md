## What and why

<!-- What does this change, and why? Link the issue it closes, for example "Closes #123". -->

## How it was tested

<!-- Commands you ran, tests you added, or manual steps. -->

## Checklist

- [ ] The pull request is small and focused on one change.
- [ ] Tests are added or updated with the behaviour they verify.
- [ ] Stock quantities change only through `StockService` ledger movements (specification 3.2).
- [ ] If the API changed: the OpenAPI snapshot is regenerated and committed (the API client is generated from it at build time).
- [ ] If the design changed: `SPECIFICATION.md` and its changelog are updated.
- [ ] `pnpm format:check` passes.
- [ ] Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).
