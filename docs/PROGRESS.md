# Progress

This file tracks what is done, what is in progress and what comes next, so each work session can continue where the last one stopped.

## Done

- Repository setup: README, AGPL-3.0 license, CLAUDE.md conventions, CODEOWNERS, SECURITY.md,
  CONTRIBUTING.md, issue and pull request templates, .editorconfig with license header, .gitattributes.
- Claude setup: project skills in `.claude/skills/` and planner, implementer, test-writer, reviewer and docs-writer agents in `.claude/agents/`.
- Solution foundation (`feature/solution-foundation`, pull request pending review), no requirement id:
  - Solution `SharpVault.slnx`, `global.json` (SDK 10.0.100, Aspire SDK 13.6.4), `Directory.Build.props` (warnings as errors, analyzers, NuGet audit) and `Directory.Packages.props` (central package management).
  - Projects: Domain (empty), Application (Mediator, Mapster and FluentValidation registration), Infrastructure (Table Storage registration), ServiceDefaults, Api, UI.Shared, Web (static shell, no authentication yet), AppHost, and the test projects `SharpVault.UnitTests`, `SharpVault.IntegrationTests` and `SharpVault.AppHost.Tests`.
  - Aspire AppHost that runs the API and the web app against a persistent Azurite container (`WithDataVolume()` and `ContainerLifetime.Persistent`).
  - Table Storage registration: `DefaultAzureCredential` with an https service URI. Connection strings, account keys and SAS are accepted only in Development, enforced at startup by a guard that does not echo configured values. The table list is empty, and tables are created once at startup.
  - Deny by default API: fallback policy that requires an authenticated user, a placeholder JwtBearer scheme that rejects every token, and only `/health` and `/alive` anonymous. OpenAPI and Scalar are mapped in Development only.
  - Health endpoints `/health` (output cached for 5 seconds) and `/alive` (process check only) in every environment, status-only bodies.
  - Security headers on the API and the web (strict CSP, `nosniff`, frame deny, referrer, permissions and cross-origin headers), no `Server` header, 1 MiB request body limit, ProblemDetails and exception handler in every environment.
  - Query strings kept out of request logs, HttpClient logs and trace tags, by code in ServiceDefaults rather than by package defaults.
  - Test infrastructure: Testcontainers Azurite factories and collections, test authentication handler, forged token helpers, storage request recorder (call counts and queries without a partition key), log capture, trace capture, reflection-based layering tests.
  - CI on GitHub Actions: build with warnings as errors, tests, coverage gate (Application and Infrastructure at 80%), vulnerable package check, CodeQL, dependency review and Dependabot.

## In progress

- `feature/encryption-core`, stacked on `feature/solution-foundation`: implemented and under review. Scope: AES-256-GCM envelope encryption, `IKeyWrapProvider` with Key Vault and development providers, unit tests, and the `SharpVault.Benchmarks` project.

## Next

The product specification is `docs/REQUIREMENTS.md`. Build in this order, one `feature/{name}` branch and pull request each:

3. `feature/entra-sign-in`: Entra ID for API and web, single-tenant and guest checks, app roles, `ICurrentUser`, users table, lock check on every request (TEN, LCK-1).
4. `feature/vault-and-folders`: secrets, folders, reveal and copy auditing, password generator, history, recycle bin (SEC, FLD).
5. `feature/sharing`: shares with colleagues, folder shares, directory search (SHR).
6. `feature/one-time-links`: one-time share links with fragment keys and browser decryption (OTL).
7. `feature/user-lock`: lock, scheduled lock, unlock, reasons (LCK, RSN).
8. `feature/offboarding-transfers`: transfer requests, second-admin approval, resumable transfer job (TRF).
9. `feature/audit-log`: audit search, filters, CSV export, user history (AUD).
10. `feature/admin-settings`: admin settings (ADM).
11. `feature/maui-app`: .NET MAUI app on the shared UI.

**Follow-ups**

- Pin GitHub Actions to commit SHAs instead of major tags (D19). Dependabot keeps the tags current until then.
- `feature/maui-app`: add `SharpVault.Maui`, add `SharpVault.Linux.slnf` (every project except Maui), switch the Linux CI job to the filter, and add a macOS or Windows job with the MAUI workload (D1).
- Deployment feature: configure HTTPS-only ingress. The foundation has no HTTPS redirect or HSTS on purpose (D9).
- `feature/entra-sign-in`: decide whether the fallback policy also requires a SharpVault role (D4), and review `form-action 'self'` for the OIDC sign-out form (D21).
- First interactive UI feature: set up Playwright and the `WebTestFactory` E2E support (D23).
- Decide on pull request SwartBergStudio/SharpVault#3 (Buy Me A Coffee badge). The README on this branch has no such badge.

**Local environment notes**

- In cloud sessions the .NET 10 SDK comes from the Ubuntu package `dotnet-sdk-10.0`, because `builds.dotnet.microsoft.com` is blocked.
