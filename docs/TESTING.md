# Testing

## Projects

| Project | What it tests | Infrastructure |
|---|---|---|
| `tests/SharpVault.UnitTests` | Storage guard, table name validation and initializer, Application registration, security headers, ServiceDefaults registration, encryption (`Infrastructure/Encryption`, `Application/Encryption`), user access gate and options (`Application/Users`), guest policy, API token and access checks (`Api/Security`), Web token and credential checks (`Web`), the theme (`UiShared/Theme`, bUnit, source scans, contrast and size budgets), and reflection-based architecture tests (`Architecture/LayeringTests.cs`, `Architecture/RepositoryConfigurationTests.cs`). It references Web with the alias `WebApp`. | xUnit, AutoFixture, NSubstitute, bUnit. No containers. |
| `tests/SharpVault.IntegrationTests` | API and Web hosts over HTTP, real Azurite, health, authorization matrix, sign-in and token validation with locally signed tokens, lock enforcement, user provisioning, the admin user list, the Web sign-in flow and account pages, security headers, error handling, logging, tracing, encryption startup and performance. | `WebApplicationFactory`, Testcontainers Azurite, AutoFixture. Docker must be running. No Entra ID call. |
| `tests/SharpVault.AppHost.Tests` | The AppHost model: Azurite lifetime, data volume, image and tag, and the development key material parameter (run mode only, API only). It builds the model with `DistributedApplicationTestingBuilder`, and it does not start the containers. | Aspire.Hosting.Testing. No Docker needed. |
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

The collection classes are named `StorageTestGroup`, `ProductionTestGroup`, `WebTestGroup` and `PerformanceTestGroup`. xUnit's CA1711 rule forbids the `Collection` suffix, so do not rename them to `...Collection`.

`ApiTestFactory` and `WebTestFactory` set the environment to Development, because the development key provider must refuse to run anywhere else. The error handling is the same in every environment (`UseExceptionHandler()`), so Development tests prove that no exception detail leaks.

## Helpers

- **`TestUsers`** and **`TestUser`** (`Authentication/`): named test identities, for example `AuthenticatedWithoutRole`. Each has an object id, display name, tenant id (`TestTenant.Id`) and roles.
- **`TestSigningKeys`**, **`TestTokens`** and **`TestTokenBuilder`**: locally generated RSA signing keys and v2 access tokens for a `TestUser`. `TestTokens.Create(user)` returns a valid token. `For(user)` returns a builder with `WithClaim`, `WithoutClaim`, `ExpiresAt`, `NotBefore` and `SignedWith` for tampering tests. The tokens are validated by the real JwtBearer and Microsoft.Identity.Web pipeline.
- **`TestIdentityProvider`**: sets the tenant-specific issuer and a static OpenID configuration for the API and the Web, so no test reaches Entra ID or the network. `TestApi` holds the API client id and scope, and `TestWebApp` holds the Web's.
- **`SpoofedTestHeaders`**: sends the old `X-Test-Oid`, `X-Test-Name`, `X-Test-Tid` and `X-Test-Roles` headers. Nothing reads them, so a caller that only sends them must get 401.
- **`CallerKind` and `CallerClients`**: the authorization matrix. `CallerKind` lists the callers (`Anonymous`, `AuthenticatedWithoutRole`, `AlgNoneToken`, `UnknownKeyToken`, and more). `CallerClients.CreateFor` builds a client for each.
- **`ForgedTokens`**: `AlgNone()` builds an unsigned `alg: none` token, and `SignedWithUnknownKey()` builds a token signed with a key the API does not trust. Both must get 401. **`TamperedTokens`** (`Support/`) covers other edited tokens.
- **`FakeTokenAcquisition`** (`Support/`): a fake `ITokenAcquisition` for the Web, so Web tests never call Entra ID for a downstream token. **`TestCookieSignInStartupFilter`** adds a test-only `/test/sign-in` route to the Web host, and **`WebSignIn.CreateClientAsync`** uses it to build a client with a signed-in session cookie.
- **`ThrowingUserStore`**, **`InterleavingUserStore`** and **`ThrowingAuditLog`** (`Support/`): failure injection and race simulation for the user access gate.
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

### Theme helpers

- `tests/SharpVault.UnitTests/UiShared/Theme/` holds the theme tests. They run in every pull request. `ThemeContrastTests` computes the contrast ratios from `app.css`, and `ThemeSourceScanTests` enforces the CSP rules in [THEME.md](THEME.md#content-security-policy).
- Run only the theme and component tests with `dotnet test SharpVault.slnx --filter "FullyQualifiedName~UiShared"`.

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

## Trait categories

- `[Trait("Category", "Security")]` marks security tests, such as threat tests and the authorization matrix.
- `[Trait("Category", "Performance")]` marks timing tests, which run only in `PerformanceTestGroup`.

## Running the tests

```bash
dotnet test SharpVault.slnx --filter "Category!=Performance"
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
```

CI runs the first command on every pull request, and the third command only on pushes to `main`. See [LOCAL-DEV.md](LOCAL-DEV.md) for coverage commands and the vulnerable package check.

## Adding tests for a feature

- Every endpoint gets an authorization matrix test across `CallerKind`.
- Every failure path is tested. Every bug fix has a regression test.
- Storage-touching tests check call counts with `StorageRequestRecorder`, and no query runs without a partition key.
- Tests that check logs use `CapturingLoggerProvider` and assert that secrets are absent.
