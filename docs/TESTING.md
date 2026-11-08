# Testing

## Projects

| Project | What it tests | Infrastructure |
|---|---|---|
| `tests/SharpVault.UnitTests` | Storage guard, table name validation and initializer, Application registration, security headers, ServiceDefaults registration, encryption (`Infrastructure/Encryption`, `Application/Encryption`), the vault (`Application/Vault`, `Infrastructure/Vaults`, `Infrastructure/Maintenance`, `PasswordGeneration`), user access gate and options (`Application/Users`), guest policy, the DTO contracts (`Dtos`), the authentication project (`Authentication`: identity, the mode resolver, the local key store, the local token issuer and accounts), API token and access checks (`Api/Security`), Web token and credential checks (`Web`), the shared UI (`UiShared`: components, layout, pages and services, with bUnit, plus the theme's source and security rules), and architecture tests in `Architecture/`. It references Web with the alias `WebApp`. | xUnit, AutoFixture, NSubstitute, bUnit. No containers. |
| `tests/SharpVault.IntegrationTests` | API and Web hosts over HTTP, real Azurite, health, authorization matrix, sign-in and token validation with locally signed tokens, lock enforcement, user provisioning, the admin user list, the Web sign-in flow and account pages, security headers, error handling, logging, tracing, encryption startup and performance, the vault API (`ApiTests/Vault`: authorisation, IDOR, mass assignment, concurrency, multi-chunk writes, purge, call counts, leakage, rate limits and local mode), and the vault timing tests. | `WebApplicationFactory`, Testcontainers Azurite, AutoFixture. Docker must be running. No Entra ID call. |
| `tests/SharpVault.AppHost.Tests` | The AppHost model: Azurite lifetime, data volume, image and tag, and the development key material parameter (run mode only, API only). It builds the model with `DistributedApplicationTestingBuilder`, and it does not start the containers. | Aspire.Hosting.Testing. No Docker needed. |
| `tests/SharpVault.E2ETests` | Browser journeys in the system Chrome (Playwright). The built API and Web run as child processes with Azurite in a container: sign-in and the vault, sharing, the admin lock and transfer path, the CSP and page-error check, the idle timeout, and the site tour that saves screenshots. See [Browser (E2E) tests](#browser-e2e-tests). | Playwright, Testcontainers Azurite, system Chrome. Docker, Chrome and a Release build of the Api and Web are needed. |
| `tests/SharpVault.Benchmarks` | BenchmarkDotNet benchmarks for the encryption hot paths. It is not a test project, so `dotnet test` does not run it. See [LOCAL-DEV.md](LOCAL-DEV.md#benchmarks). | BenchmarkDotNet. No containers. |

All test projects use xUnit v3 (`xunit.v3.mtp-off`) and run with VSTest (decision D2). Coverage is collected by coverlet.

## Integration test factories and collections

Integration tests share hosts through xUnit collections, so an expensive host (with its Azurite container) starts once per collection. Every integration test class has a `[Collection(...)]` attribute.

| Collection | Factory | Environment | Storage | Used for |
|---|---|---|---|---|
| `StorageTestGroup` | `ApiTestFactory` | Development | Azurite container (in-memory persistence) with a `StorageRequestRecorder` attached | Most API tests: health, authorization, security headers, error handling, logging, table startup |
| `ProductionTestGroup` | `ProductionApiTestFactory` | Production | No Azurite. It uses a fake `https` service URI and removes `TableStorageInitializer`, so it never calls storage | `ProductionHostingTests`: guard startup failures without echoing values, forged and test-header callers get 401 outside Development, no OpenAPI or Scalar routes, strict CSP |
| `WebTestGroup` | `WebTestFactory` | Development | None | Web host tests: shell, headers, no inline script, error page, query string logging, sign-in pages and the account page |
| `WebApiTestGroup` | `ApiTestFactory` and `WebTestFactory` | Development | Azurite (through `ApiTestFactory`) | One collection that holds both hosts, for tests that need the Web and the API together |
| `PerformanceTestGroup` | `ApiTestFactory` | Development | Its own Azurite container | Timing tests (`Category=Performance`). `DisableParallelization = true`, after 5 warm-up requests |
| `LocalModeTestGroup` | `LocalApiTestFactory` and `LocalWebTestFactory` | Development, with `AzureAd:TenantId` and `AzureAd:ClientId` set to empty | Azurite through `LocalApiTestFactory` | Local mode: the account picker, local tokens, account seeding, the loopback guard and the local cookie session. Each factory asserts local mode when its host is built |
| `LocalModeEnvironmentGroup` | `LocalApiTestFactory` | Development, as above | Azurite through `LocalApiTestFactory` | Proves that `AzureAd__TenantId` and `AzureAd__ClientId` in the process environment do not switch local mode off. `DisableParallelization = true`, because it changes process environment variables |

The collection classes are named `StorageTestGroup`, `ProductionTestGroup`, `WebTestGroup` and `PerformanceTestGroup`. xUnit's CA1711 rule forbids the `Collection` suffix, so do not rename them to `...Collection`.

`ApiTestFactory` and `WebTestFactory` set the environment to Development, because the development key provider must refuse to run anywhere else. The error handling is the same in every environment (`UseExceptionHandler()`), so Development tests prove that no exception detail leaks.

The authentication mode is read when the host is built, so a factory sets its `AzureAd` settings with `builder.UseSetting(...)` in `ConfigureWebHost`. It must not use `ConfigureAppConfiguration`, which runs after the program has already chosen the mode, so the setting would have no effect. Each Entra factory (`ApiTestFactory`, `WebTestFactory`, `ProductionApiTestFactory`) overrides `CreateHost` and calls `EntraModeAssertion.Assert`. Each local factory calls `EntraModeAssertion.AssertLocal`. A factory that lost its mode settings therefore fails when it starts, rather than running the wrong mode.

`ApiTestFactory`, `LocalApiTestFactory` and `ProductionApiTestFactory` set `Transfers:Enabled` to `false`, so the background transfer runner does not poll during a test. A test that needs the runner turns it on (`TransferHostedRunnerTests`). Other transfer tests drive the job by calling `ITransferJobRunner.RunOnceAsync`.

### Architecture tests

These are reflection and file-scan tests. They run without a host.

- `LayeringTests`: which projects may reference which, the production assembly lists, and that no production type declares an authentication handler.
- `DtoContractTests`: `SharpVault.Dtos` has no references beyond the base class library, its types are classes, enums, constants or sealed bags with auto-properties only, no credential-like property names, and `ApiRoutes` holds string constants only.
- `UiBoundaryTests`: the UI and Web sources and assemblies do not import the Application, Domain or Infrastructure namespaces. Endpoint signatures use no Domain types. The UI API clients reference routes only through `ApiRoutes`.
- `AuthenticationRepositoryTests`: the committed files contain no key for the removed single-user setting (the test scans `docs` too), no Entra tenant, client id or secret in `appsettings*.json`, no comments in the authentication sources and in the listed host files, no `System.Random`, and no file writes outside the key store.
- `PackageVersionTests`: `MudBlazor` is pinned to 9.11.0 in `Directory.Packages.props`, and only `SharpVault.UI.Shared` references it.
- `VaultSourcesTests` (`UnitTests/Application/Vault/`): source scans of `Features/Vault`. They check that no vault code references `IsAdmin`, that the mutating handlers write their audit through the committed path, and that every scanned root exists (`VaultSourceRoots_Always_AllExist`). A missing root used to be skipped silently.
- `DtoContractTests`: `VaultInputDtos_Properties_ContainNoServerControlledFields` checks that no request body has an owner, id, timestamp or revision property.

- `RepositoryConfigurationTests`: the solution lists the AppHost and test projects (`Solution_AppHostAndTestProjects_AreListed`), and every `uses:` line in `.github/workflows/*.yml` names a full 40-character commit SHA with a version note (`Workflows_EveryAction_IsPinnedToAFullCommitSha`). The test reads the files. It does not run the workflows.

Integration tests in `ApiTests/UiApiRouteContractTests.cs` check the UI's route constants against the API's mapped routes, and the verb and required query parameters of each UI call.

## Helpers

- **`TestUsers`** and **`TestUser`** (`Authentication/`): named test identities, for example `AuthenticatedWithoutRole`. Each has an object id, display name, tenant id (`TestTenant.Id`) and roles.
- **`TestSigningKeys`**, **`TestTokens`** and **`TestTokenBuilder`**: locally generated RSA signing keys and v2 access tokens for a `TestUser`. `TestTokens.Create(user)` returns a valid token. `For(user)` returns a builder with `WithClaim`, `WithoutClaim`, `ExpiresAt`, `NotBefore` and `SignedWith` for tampering tests. The tokens are validated by the real JwtBearer and Microsoft.Identity.Web pipeline.
- **`TestIdentityProvider`**: sets the tenant-specific issuer and a static OpenID configuration for the API and the Web, so no test reaches Entra ID or the network. `TestApi` holds the API client id and scope, and `TestWebApp` holds the Web's.
- **`SpoofedTestHeaders`**: sends the old `X-Test-Oid`, `X-Test-Name`, `X-Test-Tid` and `X-Test-Roles` headers. Nothing reads them, so a caller that only sends them must get 401.
- **`CallerKind` and `CallerClients`**: the authorization matrix. `CallerKind` lists the callers (`Anonymous`, `AuthenticatedWithoutRole`, `AlgNoneToken`, `UnknownKeyToken`, and more). `CallerClients.CreateFor` builds a client for each.
- **`ForgedTokens`**: `AlgNone()` builds an unsigned `alg: none` token, and `SignedWithUnknownKey()` builds a token signed with a key the API does not trust. Both must get 401. **`TamperedTokens`** (`Support/`) covers other edited tokens.
- **`FakeTokenAcquisition`** (`Support/`): a fake `ITokenAcquisition` for the Web, so Web tests never call Entra ID for a downstream token. **`TestCookieSignInStartupFilter`** adds a test-only `/test/sign-in` route to the Web host, and **`WebSignIn.CreateClientAsync`** uses it to build a client with a signed-in session cookie.
- **`ThrowingUserStore`**, **`InterleavingUserStore`** and **`ThrowingAuditLog`** (`Support/`): failure injection and race simulation for the user access gate.
- **`EntraModeAssertion`** (`Support/`): `Assert` and `AssertLocal` check the `AuthenticationModeInfo` of a built host.
- **`LocalKeyFile`** (`Factories/`): a temporary path for the local signing key, shared by the local factories, so tests never touch the developer's key file.
- **`LocalTokens`** (`Support/`): signs local tokens for a `LocalAccount` with the factory's key, for the local mode tests.
- **`StorageRequestRecorder`**: an `HttpPipelinePolicy` that the factory adds to the Azure client. It records every storage request. Use `Count` for call counts, `Requests` for details, `Clear()` between steps, and `QueriesWithoutPartitionKey()` to find table scans. `RecordedStorageRequest.IsEntityQuery` and `HasPartitionKeyCondition` drive the check.
- **`CapturingLoggerProvider`**: captures every log entry (`Entries`, with category, level, message and exception). `ContainsText(value)` checks the captured text. Use it to prove that secrets, tokens and query strings never reach logs.
- **`ActivityRecorder`** and **`RecordedActivity`**: capture ASP.NET Core and HttpClient activities and their tags, for example `url.query` and `url.full`.
- **`SecurityHeaderAssertions`**: `AssertBaseline`, `AssertApi` and `AssertWeb` check exact header values.
- **`SensitiveMarkers`**: fixed marker strings (for example `SV-SENSITIVE-EXCEPTION-7f3c9a`). Tests put them in exceptions, health checks or query strings and then assert that the markers never appear in a response or log.
- **`ThrowingHealthCheck`** and **`ThrowingRequestStartupFilter`**: failure injection for health and request pipeline tests.
- **`TestContainerImages.Azurite`**: the Azurite image used by tests. It must match the AppHost tag (`mcr.microsoft.com/azure-storage/azurite:3.35.0`), and the AppHost model test checks the match.

### Encryption helpers

- **`RecordingKeyWrapProvider`** (`UnitTests/Support/Encryption`): an in-memory `IKeyWrapProvider` that records the exact data key arrays it receives and returns. It has call counters and settable failures, so tests can prove zeroing and call counts without a vault.
- **`FakeKeyVault`**: a `CryptographyClient` subclass with real in-memory RSA-OAEP-256 keys, one per version. `Rotate()` adds a version. It records every wrap and unwrap, and it can override the returned key id, length or status.
- **`FakeKeyVaultHttpHandler`**: an `HttpMessageHandler` behind the real Key Vault SDK, through `HttpClientTransport`. It answers the challenge, key reads, `wrapKey` and `unwrapKey`, and it records each `KeyVaultHttpRequest`. Tests use it to check local versus remote wrapping, the wire algorithm, host pinning and SDK logs.
- **`StaticTokenCredential`**: returns a fixed access token, so no test reaches Entra ID.
- **`KeyMaterialLeakAssert`**: asserts that captured text (exceptions, logs) contains none of the given byte arrays, such as data keys or wrapped values.
- **`PerformanceKeyVaultHandler`**: an in-process Key Vault factory and HTTP handler for the Key Vault performance tests. It makes no network call.
- **`EncryptionMessageText`** (unit and integration): copies of the exact messages from `EncryptionMessages`, so tests compare messages without trusting the production constants.
- **`TestEncryptionOptions`**: builds options with random development key material.
- **`CountingKeyWrapProvider`** (`IntegrationTests/Encryption/`): wraps the development `IKeyWrapProvider` and counts `Wraps` and `Unwraps` with `Interlocked`. `ApiTestFactory` builds it, and it builds `EnvelopeEncryptor` over it, after it removes the registered `IEnvelopeEncryptor`. So the counts include every call the API makes. The factory exposes it as `KeyWraps`, and `Clear()` resets the counters between steps. An earlier test plan said to replace `IKeyWrapProvider` itself. The code replaces `IEnvelopeEncryptor` instead, so the counts cover the encryptor's own wrap and unwrap calls.

### Vault helpers

- **`VaultStorageRequests`** (`IntegrationTests/Vault/`): extension methods on `StorageRequestRecorder`. `VaultRequests()` returns the requests to `Vaults` and `SecretAuditLog` and the table batch requests (`POST /{table}/$batch`). `VaultWrites()` is every vault request that is not a GET, and `VaultReads()` is the GETs. `AuditWrites()` is the POSTs to `AuditLog` and `SecretAuditLog`. A transaction counts as one write, whatever its action count.
- **`PlaintextMarkers`**: one marker per field (name, username, URL, tag, value, notes, folder name), each with a random token. Tests send them in requests and then call `AssertAbsentFrom(text)` on the text they check. `VaultLeakageTests` uses it.
- **`VaultScenario`** and **`VaultEndpointCalls`**: `VaultScenario` arranges folders, secrets, a version and two binned items. `VaultEndpointCalls` sends each endpoint call by id (E1 to E20, with variants for the folder and move cases), and it lists which ids must return 404 for a non-owner. **`VaultSeeder`** writes vault rows directly through the application services and the store, without HTTP.
- **Base classes**: `VaultTestBase` (the common API setup), `VaultFunctionalTestBase` (functional tests) and `VaultSecurityTestBase` (security tests).
- **`FakeTimeProvider`** (from `Microsoft.Extensions.TimeProvider.Testing`): the purge unit tests (`RecycleBinPurgerTests`, `RecycleBinPurgeServiceTests`) and the integration test `RecycleBinPurgeTests` use it to move the clock past the 30-day retention.
- **Unit tests for the vault** are in `UnitTests/Application/Vault/` (one handler test class per feature, with `VaultHandlerTestBase` as the shared setup), `UnitTests/Infrastructure/Vaults/` (`VaultKeysTests`, `TableVaultStorePagingTests`), `UnitTests/Application/Models/ContinuationTokensTests.cs`, and `UnitTests/PasswordGeneration/`.

### Transfer and sharing helpers

- **`TransferTestBase`** (`ApiTests/Transfers/`): the base class for transfer tests, on top of `VaultTestBase`. It requests, approves and reads transfers through the API (`RequestAsync`, `ApproveAsync`, `GetTransferAsync`). `RunUntilAsync` calls the runner until a condition holds, up to `RunBound` times. `UseHost` and `UseFakeClock` register a host the base owns, and `DisposeAsync` disposes it. Other helpers: `SetUserStatusAsync`, `CountAuditEventsAsync`, `StagedRowCountAsync` (the staged rows of one transfer in the target's partition) and `CrashingTransferHost`.
- **`CrashPlan`** (`Transfers/Crash/`): fails the Nth call to one named store method, before or after its effect. `Fired` says whether the point was reached, and a test must assert it. **`CrashProxies`** wraps a store with a plan (`CrashingProxy.cs`).
- **`AuditEventCrashPlan`** (`Transfers/Crash/`): fails the first audit write of one named `AuditEventType`. A test must assert `Fired`, so a case that never reaches its event fails.
- **`HookProxy`** and **`HookedLeaseStore`** (`Transfers/Crash/`): wrappers that run a hook around a store call, or around the lease store. **`OversizeStagingProxy`** (`Transfers/Crash/`) forces chosen secrets into the oversize result of staging. **`KeyVaultFlakeBudget`** and **`FlakyKeyWrapProvider`** fail a set number of key wraps and unwraps with a transient error.
- **`SharingFunctionalTestBase`** (`ApiTests/Sharing/`): grants and shared reads through the API (`ShareSecretAsync`, `ShareFolderAsync`, `WholeSharedListAsync`, the status assertions). **`SharingSecurityTestBase`** holds the grant and rename bodies, and normalises error bodies for comparison.
- **`SharingTestApi`** (`Vault/`): a per-user HTTP client for the sharing endpoints. **`SharingMatrixEndpoints`** (`ApiTests/Sharing/`) holds the expected status for each sharing endpoint and each `CallerKind` in the authorization matrix.
- **`TestDirectoryHandler`** (`Support/`): a fake Microsoft Graph directory for recipient search and lookup, so no test calls Graph. It records every request.

### One-time link helpers

- `TestWebGateway` (`Support/TestWebGateway.cs`): the test gateway key, and `AddProof` and `AddLinkOpenProof`, which add the signed Web-to-API headers to a request. Use the link-open overload for `/api/one-time-links/{id}/open`.
- `TestAddresses` (`Support/TestAddresses.cs`): the Web host address and a visitor address, so tests can check which one an audit row holds.

### Theme helpers

- `tests/SharpVault.UnitTests/UiShared/Theme/` holds the theme's source and security tests. They run in every pull request. `ThemeSourceScanTests` enforces the CSP and markup rules in [THEME.md](THEME.md#content-security-policy), and the rules that keep revealed values out of the print and disconnected views. Colour, contrast, font, size and stylesheet-parsing tests were removed, because they pinned appearance (see [Rules](#rules)).
- bUnit tests use `MudTestContext` (in `Support/Theme/`), which registers the MudBlazor services and sets `JSRuntimeMode.Loose`. `ShellTestContext` extends it for the layout and pages.
- Run only the theme and component tests with `dotnet test SharpVault.slnx --filter "FullyQualifiedName~UiShared"`.
### Browser (E2E) tests

`tests/SharpVault.E2ETests` runs the built product in Chrome. Its trait decides where a test runs:

- `Category=E2E`: the journeys in `Journeys/` (sign-in and the vault, sharing, the admin lock and transfer path, the CSP and page-error check, the idle timeout and the sign-in landing). The `e2e` job in CI runs them on every pull request and push: `dotnet test tests/SharpVault.E2ETests -c Release --filter "Category=E2E"`.
- `Category=SiteTour`: `SiteTour`, a screenshot tour of every main screen for the admin and for User One, at 1366 by 900 and at 390 by 844. It asserts no appearance. It has only this trait, so `Category=E2E` does not select it, and the CI test step excludes it. Run it by hand to review the screens:

  ```bash
  SHARPVAULT_E2E_SCREENSHOT_DIR=/path/to/folder dotnet test tests/SharpVault.E2ETests -c Release --filter "Category=SiteTour"
  ```

  In PowerShell, set the variable with `$env:SHARPVAULT_E2E_SCREENSHOT_DIR = "C:\path\to\folder"` first. Without the variable the screenshots go to `sharpvault-site-tour` in the temp folder. The tour never clicks reveal.

Writing these tests: call `BringToFrontAsync` on the tab being driven when a test opens more than one, because Chrome starves a tab behind another when the CPU is busy. Wait for a URL with `PageObject.WaitForUrlAsync`, not `WaitForURLAsync`.

How the host works (`Hosting/E2EHost.cs`):

- It starts an Azurite container with in-memory persistence, so E2E data does not survive the run.
- It starts the built API and Web (`src/SharpVault.Api/bin/{Configuration}/net10.0` and the Web's folder) as child processes on two free loopback ports, in Development, with the local accounts, and waits for `/health`. The Web uses a temporary self-signed certificate. The process trees are killed when the host is disposed.
- It launches the system Chrome (`Channel = "chrome"`), so no Playwright browser download is needed.
- `PageErrors` records CSP violations, console errors and page errors. Every journey ends with `AssertClean`, which fails on any of them.

The E2E project references the Api and Web with `ReferenceOutputAssembly="false"`, so a build builds them, and the processes read their output from `bin`. A Release run needs Docker, Chrome and that build. The journeys check behaviour, the CSP and page errors. They do not check layout or colour.

No test calls a live Azure service. Key Vault behaviour is tested with the fakes above. A manual smoke test against a real vault is documented in [ENCRYPTION.md](ENCRYPTION.md#local-development).

`ApiTestFactory` generates random development key material for every factory instance and sets it through configuration. `ProductionApiTestFactory` sets a fixed, valid Key Vault key id for the production startup tests.

## Rules

- Test names follow `Scenario_Condition_ExpectedResult`. CA1707 is disabled under `tests/` for this reason.
- Use xUnit `Assert` only (decision D18). Do not add another assertion library.
- Assert exact status codes and exact header values.
- Pass `TestContext.Current.CancellationToken` to async calls. The xUnit analyzer `xUnit1051` requires it.
- Use `Assert.Single` or `Assert.Empty` for size checks (`xUnit2013`).
- Do not call `AddOpenApi` in a test project. The OpenAPI source generator then fails with CS9137. Configure the document with `Configure<OpenApiOptions>("v1", ...)` instead (decision D25).
- Do not call `/health` in `ProductionApiTestFactory`. It would try `DefaultAzureCredential` against a fake account (decision D27).
- Tests cover logic, data, security and behaviour. Do not add a test that pins colours, CSS, class names, icons, layout or pixel positions, exact wording (except a security warning), or framework behaviour (the DI container, Mapster, routing and static files). Appearance is reviewed by eye with the site tour. The test cleanup removed the existing appearance and framework tests; they are still in git history before this change.

## Trait categories

- `[Trait("Category", "Security")]` marks security tests, such as threat tests and the authorization matrix.
- `[Trait("Category", "Performance")]` marks timing tests, which run only in `PerformanceTestGroup`.
- `[Trait("Category", "E2E")]` marks the browser journeys in `tests/SharpVault.E2ETests`. The `e2e` CI job runs them.
- `[Trait("Category", "SiteTour")]` marks the screenshot tour. CI never runs it, and `Category=E2E` does not select it.

## Running the tests

```bash
dotnet test SharpVault.slnx --filter "Category!=Performance&Category!=E2E&Category!=SiteTour"
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
dotnet test SharpVault.slnx --filter "FullyQualifiedName~Vault"
dotnet test SharpVault.slnx --filter "FullyQualifiedName~UiShared"
```

The two `FullyQualifiedName` filters run the vault tests (API, handlers, store and UI) and the shared UI tests (components, layout, pages and theme).

The first command is the default. CI's test step runs it on every pull request and push. The `performance` job runs `Category=Performance` only on pushes to `main`. The `e2e` job runs `Category=E2E` on every pull request and push. The site tour is never run in CI. See [LOCAL-DEV.md](LOCAL-DEV.md) for coverage commands and the vulnerable package check.

## Adding tests for a feature

- Every endpoint gets an authorization matrix test across `CallerKind`.
- Every failure path is tested. Every bug fix has a regression test.
- Storage-touching tests check call counts with `StorageRequestRecorder`, and no query runs without a partition key.
- Tests that check logs use `CapturingLoggerProvider` and assert that secrets are absent.
