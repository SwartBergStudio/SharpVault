# Testing

## Projects

| Project | What it tests | Infrastructure |
|---|---|---|
| `tests/SharpVault.UnitTests` | Storage guard, table name validation and initializer, Application registration, security headers, ServiceDefaults registration, and reflection-based architecture tests (`Architecture/LayeringTests.cs`, `Architecture/RepositoryConfigurationTests.cs`). It references Web with the alias `WebApp`. | xUnit, AutoFixture, NSubstitute. No containers. |
| `tests/SharpVault.IntegrationTests` | API and Web hosts over HTTP, real Azurite, health, authorization, security headers, error handling, logging, tracing and performance. | `WebApplicationFactory`, Testcontainers Azurite, AutoFixture. Docker must be running. |
| `tests/SharpVault.AppHost.Tests` | The AppHost model: Azurite lifetime, data volume, image and tag. It builds the model with `DistributedApplicationTestingBuilder`, and it does not start the containers. | Aspire.Hosting.Testing. No Docker needed. |

All test projects use xUnit v3 (`xunit.v3.mtp-off`) and run with VSTest (decision D2). Coverage is collected by coverlet.

## Integration test factories and collections

Integration tests share hosts through xUnit collections, so an expensive host (with its Azurite container) starts once per collection. Every integration test class has a `[Collection(...)]` attribute.

| Collection | Factory | Environment | Storage | Used for |
|---|---|---|---|---|
| `StorageTestGroup` | `ApiTestFactory` | Development | Azurite container (in-memory persistence) with a `StorageRequestRecorder` attached | Most API tests: health, authorization, security headers, error handling, logging, table startup |
| `ProductionTestGroup` | `ProductionApiTestFactory` | Production | No Azurite. It uses a fake `https` service URI and removes `TableStorageInitializer`, so it never calls storage | `ProductionHostingTests`: guard startup failures without echoing values, forged and test-header callers get 401 outside Development, no OpenAPI or Scalar routes, strict CSP |
| `WebTestGroup` | `WebTestFactory` | Development | None | Web host tests: shell, headers, no inline script, error page, query string logging |
| `PerformanceTestGroup` | `ApiTestFactory` | Development | Its own Azurite container | Timing tests (`Category=Performance`). `DisableParallelization = true`, after 5 warm-up requests |

The collection classes are named `StorageTestGroup`, `ProductionTestGroup`, `WebTestGroup` and `PerformanceTestGroup`. xUnit's CA1711 rule forbids the `Collection` suffix, so do not rename them to `...Collection`.

`ApiTestFactory` and `WebTestFactory` set the environment to Development, because the development key provider must refuse to run anywhere else. The error handling is the same in every environment (`UseExceptionHandler()`), so Development tests prove that no exception detail leaks.

## Helpers

- **`TestUsers`** (`Authentication/TestUsers.cs`): named test identities, for example `AuthenticatedWithoutRole`. Each has an object id, display name, tenant id (`TestTenant.Id`) and roles.
- **`TestAuthenticationHandler`**: the `Test` authentication scheme, registered only in the test host. It reads the caller from the `X-Test-Oid`, `X-Test-Name`, `X-Test-Tid` and `X-Test-Roles` headers. `AddHeaders(client, user)` sets them. It is a test-only type, and the architecture test forbids `AuthenticationHandler` subclasses in production assemblies.
- **`CallerKind` and `CallerClients`**: the authorization matrix. `CallerKind` lists the callers (`Anonymous`, `AuthenticatedWithoutRole`, `AlgNoneToken`, `UnknownKeyToken`, and more). `CallerClients.CreateFor` builds a client for each.
- **`ForgedTokens`**: `AlgNone()` builds an unsigned `alg: none` token, and `SignedWithUnknownKey()` builds a token signed with a key the API does not trust. Both must get 401.
- **`StorageRequestRecorder`**: an `HttpPipelinePolicy` that the factory adds to the Azure client. It records every storage request. Use `Count` for call counts, `Requests` for details, `Clear()` between steps, and `QueriesWithoutPartitionKey()` to find table scans. `RecordedStorageRequest.IsEntityQuery` and `HasPartitionKeyCondition` drive the check.
- **`CapturingLoggerProvider`**: captures every log entry (`Entries`, with category, level, message and exception). `ContainsText(value)` checks the captured text. Use it to prove that secrets, tokens and query strings never reach logs.
- **`ActivityRecorder`** and **`RecordedActivity`**: capture ASP.NET Core and HttpClient activities and their tags, for example `url.query` and `url.full`.
- **`SecurityHeaderAssertions`**: `AssertBaseline`, `AssertApi` and `AssertWeb` check exact header values.
- **`SensitiveMarkers`**: fixed marker strings (for example `SV-SENSITIVE-EXCEPTION-7f3c9a`). Tests put them in exceptions, health checks or query strings and then assert that the markers never appear in a response or log.
- **`ThrowingHealthCheck`** and **`ThrowingRequestStartupFilter`**: failure injection for health and request pipeline tests.
- **`TestContainerImages.Azurite`**: the Azurite image used by tests. It must match the AppHost tag (`mcr.microsoft.com/azure-storage/azurite:3.35.0`), and the AppHost model test checks the match.

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
