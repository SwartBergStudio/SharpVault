---
name: sharpvault-security-performance-testing
description: SharpVault security, bug-prevention and performance testing. Use when planning, writing or reviewing any feature or fix, to add threat-driven security tests, regression tests, performance budgets and benchmarks. Assume every feature has a vulnerability until tests prove otherwise.
---

# Security, bug-prevention and performance testing

SharpVault stores companies' passwords. Assume every feature has a vulnerability, a bug and a performance problem until tests prove it doesn't. A feature is not done until the tests in this skill exist and pass.

## 1. Threat list in every plan

Every plan in `.github/spec/` has a **Threats** section. For each endpoint, handler or page the work touches, list how it could be abused and the test that proves it can't:

- **Access:** can someone read or change data that isn't theirs (other user, other tenant, locked user, guest, unauthenticated)?
- **Escalation:** can a user perform an admin action, or an admin bypass an approval?
- **Leakage:** can a secret value, key or token end up in a log, an error, a URL, an audit entry, a DTO or the browser cache?
- **Tampering:** can ciphertext, ids, ETags, links or request bodies be modified to change behaviour?
- **Replay and races:** can the same request, link or approval be used twice, or two requests race each other?
- **Denial of service:** can a caller force table scans, huge payloads or unbounded loops?

## 1a. Threats to remember

These apply to any change near sign-in, sessions, the UI or the idle lock. Each one names its tests, or says that a gap is open. Gaps are recorded in `docs/PROGRESS.md`.

- **Session revocation (access, replay).** Sign-out and expiry must end every open circuit, not only the browser that signed out. Tests: `Circuit_AfterSignOutElsewhere_ApiCallReturnsSignInRequired`, `Circuit_PastSessionExpiry_ApiCallReturnsSignInRequired`, `SignOut_Valid_RevokesTheSessionSoOtherCircuitsEnd`. Gap: the revocation store is per process. A second instance sees no revocation until a real distributed cache is configured, and no test runs two instances.
- **Fail-closed session check (access).** A principal with no session id, an expired session or a revoked session is not active (`Validator_MissingOrMalformedSessionClaims_IsNotActive`, `Validator_AnonymousPrincipal_IsNotActive`). If the revocation store throws, the exception propagates, so the check never returns active. No test pins the store-error case.
- **Idle lock fail-closed (access).** If the idle timer cannot run, the page must not stay unlocked. Signed-in static pages start the timer from the page: `App.razor` loads `idle-boot.js`, which starts the same module without a circuit. Interactive pages start it from the `IdleLock` component through JS interop, so they need a live circuit. Gap: if the module script is blocked or fails to load, no timer runs and nothing signs the user out. No test covers that, and the browser check is not built. Tests: `SignedInStaticPage_LoadsTheIdleBootScriptWithTheSignOutFormAndTimeout`, `AnonymousPages_NeverLoadTheIdleBootScript`, `StaticAssets_IdleMonitorModule_IsServed`, `IdleMonitorScriptTests` (runs `idle-monitor.js` under node), `IdleLock_Dispose_StopsTheModule`.
- **Locked user (access).** An existing valid token is refused on the next authorized request (`LockEnforcementTests`).
- **Admin decisions in the UI (escalation).** The UI never decides admin access from role claims. The API does, and `SideNav_CookiePrincipalClaimsAdminButApiSaysUser_HidesAdminLink` pins that.

## 2. Security tests (required)

Mark them `[Trait("Category", "Security")]` so they can be run on their own.

### Authorization matrix

For every endpoint, a theory test covers every caller below and asserts the exact status code:

| Caller | Expected |
|---|---|
| Unauthenticated | 401 |
| Token from another tenant | 401 |
| Entra guest (guests not allowed) | 403 |
| Locked user | 403 |
| User without SharpVault role | 403 |
| Owner | success |
| Share recipient with View / Edit / Manage | success only for what the permission allows |
| Other user in the same tenant | 404 (never reveal that the item exists) |
| Admin who is not the owner | 404 for secrets, success only for admin endpoints |

Build the callers once in `TestUsers` (and the local accounts in `LocalAccounts`) and reuse them.

### Required security cases

- **IDOR:** change every id in the route and body to another user's id; nothing leaks or changes.
- **Input:** oversized fields, empty and whitespace values, Unicode edge cases, characters that are illegal in Table Storage keys (`/ \ # ?` and control characters), and OData filter injection attempts (`' or PartitionKey ne '`). All are rejected by validation or handled safely.
- **No secrets in logs:** integration tests use a capturing logger provider (`CapturingLoggerProvider`). After each security test, assert that no secret value, data key or token appears in any log message or audit entry.
- **No secrets in errors:** failed requests return `ProblemDetails` without stack traces, internal ids or secret data.
- **Crypto:** round-trip; tampered ciphertext, nonce, tag or associated data fails; nonces never repeat across 100,000 encryptions; a secret encrypted for one owner can't be decrypted as another owner.
- **Concurrency:** a stale ETag returns 409 and changes nothing.
- **One-time links (planned):** a second open fails; an expired link fails; unknown, used and expired links return identical responses; the rate limit triggers; the server never stores or logs the fragment key.
- **Transfers (planned):** the requester can't approve their own request; the source or target user can't approve; two parallel approvals produce one transfer; the target sees nothing before approval; a crash mid-transfer resumes without lost or duplicated secrets.
- **Locking:** a locked user's existing valid token is rejected on the next request.
- **HTTP:** every response carries `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Strict-Transport-Security` (one year, includeSubDomains), `Permissions-Policy`, `Cross-Origin-Opener-Policy` and `Cross-Origin-Resource-Policy` (same-origin). It also gets `Content-Security-Policy` except on the paths the policy exempts, and `Cache-Control: no-store` unless the route sets one (`SecurityHeadersMiddleware`, `SecurityHeadersTests`). No CORS policy is configured, so browsers block cross-origin reads. Add an allow-list when a cross-origin client exists.
- **CSP:** the Web policy is the one built from `SecurityHeaderValues.WebContentSecurityPolicyFormat`. `Csp_OnEveryPage_MatchesTheConfiguredPolicy` checks it on the served pages, and `ForWeb_FontSrcScriptSrcImgSrcConnectSrc_RemainSelf` pins the other directives. `ThemeSourceScanTests` rejects `style` attributes, `<style>` elements, inline script, `on*` handlers on HTML elements, `javascript:` URLs and external origins in our markup. The only inline allowance is `'unsafe-inline'` in `style-src`, and it must not appear in any other directive. The browser check (planned E2E test `EveryVisitedPage_ProducesZeroCspViolationsAndNoPageErrors`) collects `securitypolicyviolation` events and console errors across the journeys. It is not built.

## 3. Bug-prevention tests

- **Every bug fix starts with a failing test** that reproduces the bug, committed in the same pull request as the fix.
- **Threat and defect tests are proved by mutation.** Remove or break the guard the test covers, or undo the fix, and the test must fail. Then restore it and confirm the test passes. A test that still passes with the guard removed proves nothing. `docs/PROGRESS.md` records the mutation result for each defect where one was done, and marks the others as unverified.
- Test the edges, not just the happy path: empty lists, one item, page boundaries, maximum lengths, null optional fields, deleted and restored items, items moved between folders.
- Validators get unit tests for every rule, both passing and failing.
- Handlers get unit tests for every `Result` failure path.
- Use AutoFixture for data, and make tests independent: each test sets up its own data and passes in any order.
- No `Thread.Sleep` or fixed delays in tests. Poll with a timeout, or use `TimeProvider` and `FakeTimeProvider` for anything time-based (expiry, scheduled locks, request expiry).

## 4. Performance

### Budgets

Each plan states performance budgets for the work. These defaults apply to the vault features, which are not built yet. They are measured in integration tests against Azurite:

| Operation | Budget |
|---|---|
| Get one secret | under 200 ms |
| List a page of 50 secrets from a vault of 5,000 | under 500 ms |
| Reveal (unwrap + decrypt) | under 200 ms |
| Transfer of 1,000 secrets | under 60 s |

Mark these tests `[Trait("Category", "Performance")]`. They seed realistic volumes and assert the budget with a generous margin, so they catch regressions without being flaky.

Budgets that exist today: the stylesheet and font sizes (`ThemeBudgetTests`), the MudBlazor stylesheet size (`ThemePerformanceTests`, under 80 KB gzip), and the Web response-time tests in the Web integration performance tests.

### No table scans and no N+1

- `StorageRequestRecorder` is an `HttpPipelinePolicy` on the table client in integration tests. It records every request and its `$filter`.
- Assert the **number of storage calls** for each endpoint (for example, list secrets: one query per page, not one per secret).
- Assert that no query runs without a `PartitionKey` condition, unless the plan explicitly allows it for an admin-only report (`QueriesWithoutPartitionKey()`).

### Benchmarks

- `tests/SharpVault.Benchmarks` uses BenchmarkDotNet with `[MemoryDiagnoser]` for hot paths: encryption, decryption, key wrapping, entity mapping, password generation.
- Benchmarks are not run in CI on every PR, but a pull request that touches a hot path includes before and after results in its description.

### Code rules that keep it fast

- Every list endpoint pages with continuation tokens and has a maximum page size.
- Pass `CancellationToken` everywhere so abandoned requests stop work.
- Reuse `TableServiceClient`, `TableClient` and `CryptographyClient` as singletons.
- Cache unwrapped data keys only in memory, only for the duration of a request, and zero them afterwards.
- Avoid loading whole vaults into memory; stream pages.

## 5. Static checks in CI

- `TreatWarningsAsErrors`, nullable enabled, `AnalysisLevel` `latest-recommended` with the .NET security analyzers.
- No paid scanning services: CodeQL and GitHub dependency review are not used, because they need GitHub Advanced Security or the dependency graph. Supply chain checks are NuGet audit (all, low, as errors) on restore and the vulnerable package step below. Dependabot version updates (free) keep packages and actions current.
- `dotnet list package --vulnerable --include-transitive` fails the build on any known vulnerable package.
- Coverage with coverlet. The CI gate (`eng/CoverageGate.cs`, run from `.github/workflows/ci.yml`) fails the build below these line-coverage minimums: Application 80, Infrastructure 80, Authentication 85, Domain.Encryption 95, Infrastructure.Encryption 95 and PasswordGeneration 95. The foundation plan (T3.10) states Authentication 90. The gate says 85. That difference is an open item in `docs/PROGRESS.md`.

## 6. Running the tests

```bash
dotnet test --filter "Category!=Performance"
dotnet test --filter "Category=Security"
dotnet test --filter "Category=Performance"
dotnet run -c Release --project tests/SharpVault.Benchmarks
```

CI runs all tests except Performance on every pull request, and Performance tests on pushes to `main`. E2E tests (`Category=E2E`) are planned, and the E2E project is not built yet. When it exists, CI's default step will exclude them and a separate job will run them.
