# Progress

This file tracks what is done, what is in progress and what comes next, so each work session can continue where the last one stopped.

## Done

- Repository setup: README, AGPL-3.0 license, CLAUDE.md conventions, CODEOWNERS, SECURITY.md, CONTRIBUTING.md, issue and pull request templates, .editorconfig with license header, .gitattributes.
- Claude setup: project skills in `.claude/skills/` and planner, implementer, test-writer, reviewer and docs-writer agents in `.claude/agents/`.
- Solution foundation (pull request SwartBergStudio/SharpVault#4, no requirement id):
  - Solution `SharpVault.slnx`, `global.json` (SDK 10.0.100, Aspire SDK 13.6.4), `Directory.Build.props` (warnings as errors, analyzers, NuGet audit) and `Directory.Packages.props` (central package management).
  - Projects: Domain, Application (Mediator, Mapster and FluentValidation registration), Infrastructure (Table Storage registration), ServiceDefaults, Api, UI.Shared, Web, AppHost, and the test projects `SharpVault.UnitTests`, `SharpVault.IntegrationTests` and `SharpVault.AppHost.Tests`.
  - Aspire AppHost that runs the API and the web app against a persistent Azurite container (`WithDataVolume()` and `ContainerLifetime.Persistent`).
  - Table Storage registration with `DefaultAzureCredential` and an https service URI. Connection strings, account keys and SAS are accepted only in Development, enforced at startup by a guard that does not echo configured values.
  - Deny by default API, health endpoints `/health` and `/alive`, security headers on the API and the web (strict CSP, `nosniff`, frame deny, referrer, permissions and cross-origin headers, no `Server` header), 1 MiB request body limit, ProblemDetails and exception handler in every environment.
  - Query strings kept out of request logs, HttpClient logs and trace tags.
  - Test infrastructure: Testcontainers Azurite factories and collections, storage request recorder (call counts and queries without a partition key), log capture, trace capture, reflection-based layering tests.
  - CI on GitHub Actions: build with warnings as errors, tests, coverage gate, vulnerable package check and Dependabot version updates.
- Encryption core (pull request SwartBergStudio/SharpVault#5, no requirement id). Details in `docs/ENCRYPTION.md`:
  - AES-256-GCM envelopes (version 1) with a random data key per item. The purpose, item id and owner id are bound as associated data.
  - Key wrap providers: a Key Vault provider (RSA-OAEP-256 through the app's managed identity, tested with fakes) and a development provider that refuses to run outside the Development environment.
  - The AppHost generates the development key material once and stores it in its user secrets.
  - Benchmarks project for the encryption hot paths (not a test project), and the coverage gate at 95% for the encryption namespaces.
  - No entity stores a secret yet. Key Vault itself is not provisioned.
- CodeQL removal (pull request SwartBergStudio/SharpVault#10). CodeQL and dependency review needed paid GitHub features, so they are no longer in CI. CI is build, tests, coverage gate and the vulnerable package check.
- Entra sign-in, this pull request (`feature/sign-in-theme-and-vault-plan`, plan `docs/plans/entra-sign-in.md`). Setup steps in `docs/ENTRA-SETUP.md`:
  - API: Microsoft.Identity.Web JWT validation of v2 access tokens for the `access_as_user` scope, with a `tid` check against the configured tenant (TEN-1, TEN-2).
  - Web: OpenID Connect with code flow and PKCE, a hardened session cookie, and token acquisition for the API with an encrypted token cache.
  - App roles `SharpVault.User` and `SharpVault.Admin` required on every API request, with guests rejected unless `Tenancy:AllowGuests` is set (TEN-3, TEN-4).
  - Users table: a row is created on first use and updated on sign-in, with ETag concurrency (TEN-5). Sign-in is audited once per interval.
  - The lock status is checked on every authorized request, with a short cache (LCK-1, read side only, no lock command yet).
  - Append-only audit log writer (`IAuditLog`), with sign-in as the first event.
  - Endpoints: `GET /api/me` (User role) and `GET /api/admin/users` (Admin role, paged, directory data only).
  - Web pages: account, access denied, signed out and sign-in error.
  - Tests use locally signed tokens validated by the real JwtBearer pipeline, so the suite runs without Entra ID.
- Vault Steel theme, this pull request (plan `docs/plans/app-theme.md`, no requirement id, supports SEC-3, ADM-4, LCK-1 and OTL-3). Details in `docs/THEME.md`:
  - Design tokens for dark and light schemes, with WCAG 2.2 AA contrast checked from the stylesheet.
  - Self-hosted Archivo and JetBrains Mono (OFL 1.1) and 31 inline SVG icons (Lucide, ISC).
  - Shared components (`Icon`, `VaultDoor`, `StatusBadge`, `Alert`, `EmptyState`, `DialogShell`, `SecretFrame`, `BusyIndicator`) and the layout shell, with fixed ids.
  - Strict CSP kept: no inline style or script, no external origins.
  - Source scans, bUnit, contrast and size budget tests under `UnitTests/UiShared/Theme`.
- Vault and folders plan, this pull request (`docs/plans/vault-and-folders.md`). Plan only, nothing implemented.
- JS interop rule, this pull request, recorded in `CLAUDE.md`. Blazor JS interop is allowed only for specific browser methods that .NET cannot call directly (for example the clipboard, or Web Crypto for one-time links), wrapped behind a C# interface. No JavaScript framework is used.

## In progress

None.

## Next

Start with item 4. The other items follow in build order.

4. `feature/vault-and-folders`, branched from `main` after this pull request merges. Implement `docs/plans/vault-and-folders.md`. Its T0 merge prerequisite is satisfied once this pull request is merged. Scope: secrets, folders, reveal and copy auditing, password generator, history, recycle bin (SEC, FLD).
5. `feature/sharing`: shares with colleagues, folder shares, directory search (SHR).
6. `feature/one-time-links`: one-time share links with fragment keys and browser decryption (OTL).
7. `feature/user-lock`: lock, scheduled lock, unlock, reasons (LCK, RSN). It must call `IUserAccessCache.Invalidate` after it writes a lock.
8. `feature/offboarding-transfers`: transfer requests, second-admin approval, resumable transfer job (TRF).
9. `feature/audit-log`: audit search, filters, CSV export, user history (AUD).
10. `feature/admin-settings`: admin settings (ADM).
11. `feature/maui-app`: .NET MAUI app on the shared UI.

**Follow-ups**

- Configure a real Entra tenant and check sign-in end to end, following `docs/ENTRA-SETUP.md`. Sign-in cannot be exercised before then.
- Deployment: managed identity federated credential for the Web, Data Protection key storage, forwarded headers, and HTTPS-only ingress. The foundation has no HTTPS redirect or HSTS on purpose (D9).
- Deployment: provision the Key Vault key, the role assignment (Key Vault Crypto User), the Table Storage role (Storage Table Data Contributor) and `Encryption__KeyVaultKeyId` for each environment (see `docs/ENCRYPTION.md`).
- Pin GitHub Actions to commit SHAs instead of major tags (D19). Dependabot keeps the tags current until then.
- `feature/maui-app`: add `SharpVault.Maui`, add `SharpVault.Linux.slnf` (every project except Maui), switch the Linux CI job to the filter, add a macOS or Windows job with the MAUI workload (D1). Also follow the MAUI notes in `docs/THEME.md`.
- Playwright E2E with system Chrome, and the `WebTestFactory` E2E support (D23). This also covers the skip link and dialog focus checks (theme D6, D14).
- Theme: drop `data:` from `img-src` (theme D8).
- Key rewrap maintenance job using `RewrapDataKeyAsync` (D8). Old Key Vault key versions must stay enabled until it has migrated every wrapped key.
- D5 and D5a are decided in `docs/plans/vault-and-folders.md` (D1, D2): one metadata key per user, a key per secret for values and notes, and a cross-session `ReEncryptTo` added by `feature/offboarding-transfers`.
- `feature/vault-and-folders`: give every history entry its own item id (D24), so a history envelope cannot replace the current value.
- Ask to allow learn.microsoft.com in the session to confirm Key Vault throttling limits (D27).
- `feature/user-lock`: audit denied attempts by locked users (sign-in D30), and end web sessions on lock with Graph `revokeSignInSessions` plus a web-side check (D31, LCK-3).
- `feature/admin-settings`: replace `ConfiguredGuestAccessPolicy` with the stored admin setting (sign-in D5, ADM-1).
- Before scaling out the Web: add a shared distributed token cache (sign-in D24), not only shared Data Protection keys.
- Add `AccessGateBenchmarks` and `SignedInIdentityReaderBenchmarks` to `tests/SharpVault.Benchmarks` (sign-in D27).
- `feature/maui-app`: replace the web sign-out form with MSAL sign-out (sign-in D14).
- Hardening: build the skip link href as an absolute path; give the account page a fallback display name and a guard against a 401 sign-in loop; make the zero value of `StoreWriteResult` fail-closed; strip lone surrogates anywhere in `UserProfileText.Clean`; keep paging in `TableUserStore.ListAsync` when a page is empty but has a continuation token.
- Deployment: configure host filtering (`AllowedHosts` is `*`, and the OIDC redirect URI is built from the Host header).
- Resolved: foundation follow-ups D4 (fallback policy now requires a role) and D21 (CSP `form-action` includes the Entra origin).

**Local environment notes**

- In cloud sessions the .NET 10 SDK comes from the Ubuntu package `dotnet-sdk-10.0`, because `builds.dotnet.microsoft.com` is blocked. Install it with apt.
- In cloud sessions, start Docker before the integration tests. If `dockerd` reports that its PID is still running, remove the stale `/var/run/docker.pid` and start it again.
