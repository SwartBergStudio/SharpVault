# Progress

This file tracks what is done and what comes next, so each work session can continue where the last one stopped.

## Done

- Repository setup: README, AGPL-3.0 license, CLAUDE.md conventions, CODEOWNERS, SECURITY.md,
  CONTRIBUTING.md, issue and pull request templates, .editorconfig with license header, .gitattributes.
- Claude setup: project skills in `.claude/skills/` and planner, implementer, test-writer, reviewer and docs-writer agents in `.claude/agents/`.

## Next

The product specification is `docs/REQUIREMENTS.md`. Build in this order, one `feature/{name}` branch and pull request each:

1. `feature/solution-foundation`: .NET 10 solution scaffold, Directory.Build.props, Directory.Packages.props, Aspire AppHost with persistent Azurite, CI workflow.
2. `feature/encryption-core`: AES-256-GCM envelope encryption, `IKeyWrapProvider` with Key Vault and development providers, unit tests.
3. `feature/entra-sign-in`: Entra ID for API and web, single-tenant and guest checks, app roles, `ICurrentUser`, users table, lock check on every request (TEN, LCK-1).
4. `feature/vault-and-folders`: secrets, folders, reveal and copy auditing, password generator, history, recycle bin (SEC, FLD).
5. `feature/sharing`: shares with colleagues, folder shares, directory search (SHR).
6. `feature/one-time-links`: one-time share links with fragment keys and browser decryption (OTL).
7. `feature/user-lock`: lock, scheduled lock, unlock, reasons (LCK, RSN).
8. `feature/offboarding-transfers`: transfer requests, second-admin approval, resumable transfer job (TRF).
9. `feature/audit-log`: audit search, filters, CSV export, user history (AUD).
10. `feature/admin-settings`: admin settings (ADM).
11. `feature/maui-app`: .NET MAUI app on the shared UI.
