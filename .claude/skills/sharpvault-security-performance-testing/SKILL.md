---
name: sharpvault-security-performance-testing
description: SharpVault security, bug-prevention and performance testing. Use when planning, writing or reviewing any feature or fix, to add threat-driven security tests, regression tests, performance budgets and benchmarks. Assume every feature has a vulnerability until tests prove otherwise.
---

# Security, bug-prevention and performance testing

SharpVault stores companies' passwords. Assume every feature has a vulnerability, a bug and a performance problem until tests prove it doesn't. A feature is not done until the tests in this skill exist and pass.

## 1. Threat list in every plan

Every plan in `docs/plans/` has a **Threats** section. For each endpoint, handler or page the work touches, list how it could be abused and the test that proves it can't:

- **Access:** can someone read or change data that isn't theirs (other user, other tenant, locked user, guest, unauthenticated)?
- **Escalation:** can a user perform an admin action, or an admin bypass an approval?
- **Leakage:** can a secret value, key or token end up in a log, an error, a URL, an audit entry, a DTO or the browser cache?
- **Tampering:** can ciphertext, ids, ETags, links or request bodies be modified to change behaviour?
- **Replay and races:** can the same request, link or approval be used twice, or two requests race each other?
- **Denial of service:** can a caller force table scans, huge payloads or unbounded loops?

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

Build the callers once in the test authentication handler (`TestUsers.Owner`, `TestUsers.OtherUser`, `TestUsers.LockedUser`...) and reuse them.

### Required security cases

- **IDOR:** change every id in the route and body to another user's id; nothing leaks or changes.
- **Input:** oversized fields, empty and whitespace values, Unicode edge cases, characters that are illegal in Table Storage keys (`/ \ # ?` and control characters), and OData filter injection attempts (`' or PartitionKey ne '`). All are rejected by validation or handled safely.
- **No secrets in logs:** integration tests register a capturing `ILoggerProvider`; after each security test, assert that no secret value, data key or token appears in any log message or audit entry.
- **No secrets in errors:** failed requests return `ProblemDetails` without stack traces, internal ids or secret data.
- **Crypto:** round-trip; tampered ciphertext, nonce, tag or associated data fails; nonces never repeat across 100,000 encryptions; a secret encrypted for one owner can't be decrypted as another owner.
- **Concurrency:** a stale ETag returns 409 and changes nothing.
- **One-time links:** a second open fails; an expired link fails; unknown, used and expired links return identical responses; the rate limit triggers; the server never stores or logs the fragment key.
- **Transfers:** the requester can't approve their own request; the source or target user can't approve; two parallel approvals produce one transfer; the target sees nothing before approval; a crash mid-transfer resumes without lost or duplicated secrets.
- **Locking:** a locked user's existing valid token is rejected on the next request.
- **HTTP:** security headers are present (CSP, HSTS, X-Content-Type-Options, Referrer-Policy, frame-ancestors); CORS allows only configured origins; secret responses send `Cache-Control: no-store`.

## 3. Bug-prevention tests

- **Every bug fix starts with a failing test** that reproduces the bug, committed in the same pull request as the fix.
- Test the edges, not just the happy path: empty lists, one item, page boundaries, maximum lengths, null optional fields, deleted and restored items, items moved between folders.
- Validators get unit tests for every rule, both passing and failing.
- Handlers get unit tests for every `Result` failure path.
- Use AutoFixture for data, and make tests independent: each test sets up its own data and passes in any order.
- No `Thread.Sleep` or fixed delays in tests. Poll with a timeout, or use `TimeProvider` and `FakeTimeProvider` for anything time-based (expiry, scheduled locks, request expiry).

## 4. Performance

### Budgets

Each plan states performance budgets for the work. Defaults, measured in integration tests against Azurite:

| Operation | Budget |
|---|---|
| Get one secret | under 200 ms |
| List a page of 50 secrets from a vault of 5,000 | under 500 ms |
| Reveal (unwrap + decrypt) | under 200 ms |
| Transfer of 1,000 secrets | under 60 s |

Mark these tests `[Trait("Category", "Performance")]`. They seed realistic volumes and assert the budget with a generous margin, so they catch regressions without being flaky.

### No table scans and no N+1

- Register a counting `HttpPipelinePolicy` on the `TableServiceClient` in integration tests. It records every request and its `$filter`.
- Assert the **number of storage calls** for each endpoint (for example, list secrets: one query per page, not one per secret).
- Assert that no query runs without a `PartitionKey` condition, unless the plan explicitly allows it for an admin-only report.

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
- Coverage with coverlet: at least 80% line coverage for Application and Infrastructure, and at least 95% for encryption code. The build fails below these.

## 6. Running the tests

```bash
dotnet test --filter "Category!=Performance"
dotnet test --filter "Category=Security"
dotnet test --filter "Category=Performance"
dotnet run -c Release --project tests/SharpVault.Benchmarks
```

CI runs all tests except Performance on every pull request, and Performance tests on pushes to `main`.
