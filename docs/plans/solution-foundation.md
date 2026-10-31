# Plan: solution foundation

- **Branch**: `feature/solution-foundation` (already created from `main`)
- **PROGRESS.md item**: Next, item 1
- **Planner**: verified every package version, API call and test technique below in a throwaway project on .NET SDK 10.0.114 (Linux) before writing this plan. Where the plan says "verified", the implementer can rely on it.

## 1. Goal

Create the .NET 10 solution that every later feature builds on:

- Projects: `SharpVault.Domain`, `SharpVault.Application`, `SharpVault.Infrastructure`, `SharpVault.Api`, `SharpVault.UI.Shared`, `SharpVault.Web`, `SharpVault.ServiceDefaults`, `SharpVault.AppHost`, and the tests `SharpVault.UnitTests`, `SharpVault.IntegrationTests` and `SharpVault.AppHost.Tests`.
- Shared build settings: `global.json`, `Directory.Build.props` (warnings as errors, analyzers, NuGet audit) and `Directory.Packages.props` (central package management).
- An Aspire AppHost that runs the API and web app against a persistent Azurite container (`WithDataVolume()` and `ContainerLifetime.Persistent`).
- `TableServiceClient` registration: `DefaultAzureCredential` with an https service URI everywhere, and Azurite or other connection strings only in Development, enforced at startup.
- Anonymous, status-only health endpoints (`/health`, `/alive`). OpenAPI and Scalar in Development only.
- A deny-by-default authorization baseline for the API: a fallback policy that requires an authenticated user, a fail-closed default scheme, and only the health endpoints explicitly anonymous.
- A security headers baseline for the API and web, a ProblemDetails error baseline, and request size limits.
- An integration test fixture with Testcontainers Azurite for later features to reuse, plus a test authentication handler, a storage request recorder (call counts and table-scan detection) and a log capture provider.
- Reflection-based architecture (layering) tests.
- CI on GitHub Actions: build with warnings as errors, tests, coverage thresholds, vulnerable package check, CodeQL, dependency review and Dependabot.

### Out of scope (later items in PROGRESS.md)

- Entra ID sign-in, `ICurrentUser`, app roles and `Policies`, tenant, guest and lock checks (`feature/entra-sign-in`). The foundation registers a placeholder JwtBearer scheme that rejects every token (decision D4).
- Encryption, `IKeyWrapProvider` and `tests/SharpVault.Benchmarks` (`feature/encryption-core`).
- Any store interface, table, entity, request/handler, `Result<T>` type or pipeline behavior (decision D13).
- `SharpVault.Maui` (decision D1).
- Playwright E2E tests (decision D23).
- Deployment and infrastructure as code (HTTPS-only ingress, managed identity role assignments).
- The visual theme and fonts (decision D20).

## 2. Requirements

**This work delivers no `docs/REQUIREMENTS.md` id.** It is infrastructure. It enables:

| Id / rule | What the foundation provides |
|---|---|
| CLAUDE.md Storage | `TableServiceClient` through Aspire with `DefaultAzureCredential`. Account keys, SAS and connection strings are rejected outside Development. Tables are created on start, not per request. |
| CLAUDE.md Auth, VIS-1, VIS-4 | Deny by default: a fallback policy requiring authentication, an endpoint inventory test, and only health endpoints anonymous. |
| TEN-1..TEN-4, LCK-1 | The authentication and authorization slot and pipeline order that `feature/entra-sign-in` fills. The lock check middleware goes directly after `UseAuthorization()` (section 4, T6). |
| OTL-3 | A strict CSP baseline. Query strings are kept out of logs and traces by our own code in ServiceDefaults, not by package defaults, which configuration can switch off (TH25). No HTTP body or header logging. |
| AUD-3 | Log capture test infrastructure, so every later feature can assert that no secrets appear in logs. |
| CLAUDE.md Local dev | AppHost with persistent Azurite. Everything except Maui builds and tests on Linux. |

Pull request and test references should say "foundation (no requirement id)".

## 3. Verified versions

All versions are stable on nuget.org as of 2026-10-31 and were restored and built here.

| Package | Version | Used by |
|---|---|---|
| `Aspire.AppHost.Sdk` (MSBuild SDK, pinned in `global.json`) | 13.6.4 | AppHost |
| `Aspire.Hosting.Azure.Storage` | 13.6.4 | AppHost |
| `Aspire.Hosting.Testing` | 13.6.4 | AppHost.Tests |
| `Aspire.Azure.Data.Tables` | 13.6.4 | Infrastructure |
| `Azure.Data.Tables` | 12.13.0 | Infrastructure |
| `Azure.Identity` | 1.21.0 | Infrastructure |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.14 | Infrastructure |
| `SwartBerg.Mediator` (namespace `Mediator`) | 4.0.0 | Application |
| `Mapster`, `Mapster.DependencyInjection` | 10.0.13 | Application |
| `FluentValidation`, `FluentValidation.DependencyInjectionExtensions` | 12.2.0 | Application |
| `Microsoft.AspNetCore.OpenApi` | 10.0.14 | Api |
| `Scalar.AspNetCore` | 2.18.7 | Api |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.14 | Api |
| `Microsoft.AspNetCore.Components.Web` | 10.0.14 | UI.Shared |
| `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.ServiceDiscovery` | 10.11.0 | ServiceDefaults |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.Runtime` | 1.20.1 | ServiceDefaults |
| `xunit.v3.mtp-off` | 4.0.2 | all tests (decision D2) |
| `xunit.runner.visualstudio` | 4.0.1 | all tests |
| `Microsoft.NET.Test.Sdk` | 18.11.0 | all tests |
| `coverlet.collector` | 10.1.0 | all tests |
| `AutoFixture` | 4.18.1 | Unit, Integration |
| `AutoFixture.Xunit3` | 4.19.0 | Unit |
| `NSubstitute` | 6.2.0 | Unit |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.14 | Integration |
| `Testcontainers.Azurite` | 4.16.0 | Integration |
| `dotnet-reportgenerator-globaltool` (local tool) | 5.5.12 | CI coverage merge |

- Azurite image: `mcr.microsoft.com/azure-storage/azurite:3.35.0`. This is the tag Aspire 13.6.4 uses, so the AppHost and the tests run the same Azurite.
- `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for this set.

## 4. Target layout

```
SharpVault.slnx
global.json
Directory.Build.props
Directory.Packages.props
.config/dotnet-tools.json
eng/CoverageGate.cs
.github/workflows/ci.yml
.github/workflows/codeql.yml
.github/workflows/dependency-review.yml
.github/dependabot.yml
src/
  SharpVault.Domain/
  SharpVault.Application/
  SharpVault.Infrastructure/
  SharpVault.ServiceDefaults/
  SharpVault.Api/
  SharpVault.UI.Shared/
  SharpVault.Web/
  SharpVault.AppHost/
tests/
  Directory.Build.props
  .editorconfig
  coverage.runsettings
  SharpVault.UnitTests/
  SharpVault.IntegrationTests/
  SharpVault.AppHost.Tests/
```

Every `.cs` file starts with the exact header from `.editorconfig`:

```
// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.
```

The build fails without it (IDE0073 plus `EnforceCodeStyleInBuild`, verified). Razor, JS and CSS files carry no comments at all. The .NET templates contain comments that must be deleted.

## 5. Tasks

Ownership: tasks T1 to T9 go to `implementer`, T10 to T13 to `test-writer`, and T14 to `docs-writer`.

Order and parallelism:

```
T1 (alone, first)
 ├─ T2, T3, T4, T5, T7, T9  (parallel after T1)
 ├─ T6  (after T2, T3, T4)
 ├─ T8  (after T4 and T5; independent of T6)
 ├─ T10 (after T3, T6)   T11 (after T10)   T12 (after T7)   T13 (after T2, T3, T4)
 └─ T14 (after all)
```

Each implementer runs `dotnet build SharpVault.slnx` before finishing, and it must report `0 Warning(s)` and `0 Error(s)`.

---

### T1. Build scaffolding, solution and empty projects (sequential, first)

Creates every project file so that later tasks only add source files and never edit the solution at the same time.

**Files to create**

1. `global.json`

   ```json
   {
     "sdk": { "version": "10.0.100", "rollForward": "latestFeature", "allowPrerelease": false },
     "msbuild-sdks": { "Aspire.AppHost.Sdk": "13.6.4" }
   }
   ```

   Do not add a `test` runner section, so `dotnet test` stays on VSTest (D2).

2. `Directory.Build.props`

   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <LangVersion>latest</LangVersion>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
       <AnalysisLevel>latest-recommended</AnalysisLevel>
       <AnalysisLevelSecurity>latest-all</AnalysisLevelSecurity>
       <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
       <NuGetAudit>true</NuGetAudit>
       <NuGetAuditMode>all</NuGetAuditMode>
       <NuGetAuditLevel>low</NuGetAuditLevel>
       <Deterministic>true</Deterministic>
       <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
       <IsPackable>false</IsPackable>
       <Authors>SwartBergStudio</Authors>
       <Company>SwartBergStudio</Company>
       <Copyright>Copyright (c) SwartBergStudio</Copyright>
       <PackageLicenseExpression>AGPL-3.0-only</PackageLicenseExpression>
     </PropertyGroup>
   </Project>
   ```

3. `Directory.Packages.props`: `ManagePackageVersionsCentrally` = `true` and `CentralPackageTransitivePinningEnabled` = `true`, with one `<PackageVersion>` per package in section 3, except `Aspire.AppHost.Sdk` and the local tool.
   - Do **not** add `Aspire.Hosting.AppHost`. The AppHost SDK references it implicitly, and listing it fails restore with NU1009 (verified).
   - Sort the entries alphabetically.

4. `.config/dotnet-tools.json`: a tool manifest with `dotnet-reportgenerator-globaltool` 5.5.12 (`dotnet new tool-manifest`, then `dotnet tool install dotnet-reportgenerator-globaltool --version 5.5.12`).

5. `tests/Directory.Build.props`

   ```xml
   <Project>
     <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
     <PropertyGroup>
       <OutputType>Exe</OutputType>
       <IsTestProject>true</IsTestProject>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="xunit.v3.mtp-off" />
       <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
       <PackageReference Include="Microsoft.NET.Test.Sdk" />
       <PackageReference Include="coverlet.collector" PrivateAssets="all" />
     </ItemGroup>
   </Project>
   ```

6. `tests/.editorconfig`: not `root`. It contains only:

   ```
   [*.cs]
   dotnet_diagnostic.CA1707.severity = none
   ```

   CA1707 rejects underscores and fires on `Scenario_Condition_ExpectedResult` names (verified, D16).

7. `tests/coverage.runsettings`: a coverlet `XPlat code coverage` data collector with:
   - `Format` `cobertura`
   - `Include` `[SharpVault.*]*`
   - `Exclude` `[SharpVault.*Tests]*,[SharpVault.AppHost]*`
   - `ExcludeByAttribute` `GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute`
   - `ExcludeByFile` `**/obj/**/*.cs`
   - `SkipAutoProps` `true`

8. Project files. Use `../` relative paths and no `Version` attributes.

   | Project | SDK | Package references | Project references |
   |---|---|---|---|
   | `src/SharpVault.Domain/SharpVault.Domain.csproj` | `Microsoft.NET.Sdk` | none | none |
   | `src/SharpVault.Application/SharpVault.Application.csproj` | `Microsoft.NET.Sdk` | SwartBerg.Mediator, Mapster, Mapster.DependencyInjection, FluentValidation, FluentValidation.DependencyInjectionExtensions | Domain |
   | `src/SharpVault.Infrastructure/SharpVault.Infrastructure.csproj` | `Microsoft.NET.Sdk` | Aspire.Azure.Data.Tables, Azure.Data.Tables, Azure.Identity, Microsoft.Extensions.Hosting.Abstractions | Application |
   | `src/SharpVault.ServiceDefaults/SharpVault.ServiceDefaults.csproj` | `Microsoft.NET.Sdk`, `<IsAspireSharedProject>true</IsAspireSharedProject>`, `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | Http.Resilience, ServiceDiscovery, the five OpenTelemetry packages | none |
   | `src/SharpVault.Api/SharpVault.Api.csproj` | `Microsoft.NET.Sdk.Web` | Microsoft.AspNetCore.OpenApi, Scalar.AspNetCore, Microsoft.AspNetCore.Authentication.JwtBearer | Application, Infrastructure, ServiceDefaults |
   | `src/SharpVault.UI.Shared/SharpVault.UI.Shared.csproj` | `Microsoft.NET.Sdk.Razor`, `<SupportedPlatform Include="browser" />` | Microsoft.AspNetCore.Components.Web | none |
   | `src/SharpVault.Web/SharpVault.Web.csproj` | `Microsoft.NET.Sdk.Web`, `<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>` | none | UI.Shared, ServiceDefaults |
   | `src/SharpVault.AppHost/SharpVault.AppHost.csproj` | `Aspire.AppHost.Sdk` (no version in the attribute, it comes from `global.json`), `<OutputType>Exe</OutputType>`, `<UserSecretsId>` (a new GUID from `uuidgen`), `<NoWarn>$(NoWarn);ASPIRE010</NoWarn>` | Aspire.Hosting.Azure.Storage | Api, Web |
   | `tests/SharpVault.UnitTests/SharpVault.UnitTests.csproj` | `Microsoft.NET.Sdk` | AutoFixture, AutoFixture.Xunit3, NSubstitute | Domain, Application, Infrastructure, ServiceDefaults, Api, UI.Shared, and Web with `Aliases="WebApp"` |
   | `tests/SharpVault.IntegrationTests/SharpVault.IntegrationTests.csproj` | `Microsoft.NET.Sdk` | AutoFixture, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.Azurite | Api, Infrastructure, and Web with `Aliases="WebApp"` |
   | `tests/SharpVault.AppHost.Tests/SharpVault.AppHost.Tests.csproj` | `Microsoft.NET.Sdk` | Aspire.Hosting.Testing | AppHost, Infrastructure |

   - The Api and the Web both have a compiler-generated public `Program` in the global namespace. .NET 10 makes it public automatically, so do **not** write `public partial class Program;` (verified).
   - The `WebApp` alias keeps the two `Program` types apart in test projects (verified, D17).

9. Minimal entry points so the solution builds. T5, T6 and T7 replace them.
   - `src/SharpVault.Api/Program.cs` and `src/SharpVault.Web/Program.cs`: `var builder = WebApplication.CreateBuilder(args); await builder.Build().RunAsync();`
   - `src/SharpVault.AppHost/AppHost.cs`: `await DistributedApplication.CreateBuilder(args).Build().RunAsync();`
   - Each test project gets `GlobalUsings.cs` containing `global using Xunit;`.

10. `SharpVault.slnx`: `dotnet new sln -n SharpVault` (creates `.slnx` on SDK 10), then `dotnet sln SharpVault.slnx add` for every project above. Projects go into the `/src/` and `/tests/` solution folders. Add a `/build/` folder listing `global.json`, `Directory.Build.props`, `Directory.Packages.props` and `.editorconfig` as `<File>` items.

**Acceptance**

- `dotnet restore SharpVault.slnx` and `dotnet build SharpVault.slnx` succeed with `0 Warning(s)`.
- `dotnet test SharpVault.slnx` runs and finds 0 tests without failing.
- No `Version=` attribute appears in any `.csproj`.
- `git grep -n "PackageVersion Include=\"Aspire.Hosting.AppHost\""` finds nothing.

---

### T2. Domain and Application layer registration (parallel with T3, T4, T5, T7, T9)

**Files**

- `src/SharpVault.Domain/`: no source files. The project stays empty until a feature adds entities. Do not add marker classes.
- `src/SharpVault.Application/DependencyInjection.cs`: `public static class DependencyInjection` with `public static IServiceCollection AddApplication(this IServiceCollection services)`, which does the following in order:
  1. `var assembly = typeof(DependencyInjection).Assembly;`
  2. `services.AddValidatorsFromAssembly(assembly);`
  3. `services.AddMediator(options => options.EnablePersistence = false, assembly);`. This needs `using Mediator;`. The overload `AddMediator(IServiceCollection, Action<Mediator.Core.MediatorOptions>, params Assembly[])` exists (verified).
  4. `services.AddApplicationMappings();`
  5. `return services;`
- `src/SharpVault.Application/Mapping/MappingConfig.cs`: `public static class MappingConfig` with `AddApplicationMappings(this IServiceCollection services)`. It calls `TypeAdapterConfig.GlobalSettings.Scan(typeof(MappingConfig).Assembly)`, registers that config as a singleton, registers `services.AddScoped<IMapper, ServiceMapper>()` and returns `services`.

**Acceptance**

- The build is clean.
- No pipeline behaviors, `Result<T>`, `ICurrentUser` or store interfaces are added (D13).

---

### T3. Infrastructure: Table Storage registration, guard and initializer (parallel with T2, T4, T5, T7, T9)

**Files** (namespace `SharpVault.Infrastructure` and `SharpVault.Infrastructure.Storage`):

1. `Storage/TableStorageConnection.cs`: `public static class TableStorageConnection { public const string Name = "tables"; }`
2. `Storage/TableNames.cs`: `public static class TableNames { public static IReadOnlyList<string> All { get; } = []; }`. Later features add a constant per table and append it to `All`.
3. `Storage/TableStorageMessages.cs`: `internal static class` with these `const string` messages. **None of them may contain a configured value.**
   - `NotConfigured` = `"Table Storage is not configured. Set ConnectionStrings:tables to the Table Storage service URI."`
   - `ConnectionStringNotAllowed` = `"Outside the Development environment Table Storage must use an https service URI with Microsoft Entra ID authentication. Connection strings, account keys and SAS tokens are not allowed."`
   - `ServiceUriNotAllowed` = `"Outside the Development environment the Table Storage service URI must be an absolute https URI without a query string or user information."`
   - `InvalidTableName` = `"Table names must be 3 to 63 letters or digits and start with a letter."`
   - `ReservedTableName` = `"The table name 'tables' is reserved by Azure Table Storage."`
   - `DuplicateTableName` = `"Table names must be unique, ignoring case."`
4. `Storage/TableStorageSettingsGuard.cs`: `public static class TableStorageSettingsGuard` with `public static void EnsureAllowed(AzureDataTablesSettings settings, IHostEnvironment environment)`. It runs these rules in order and throws `InvalidOperationException` with the message named:
   1. If `settings.ConnectionString` is null or whitespace and `settings.ServiceUri` is null, throw `NotConfigured`. This applies in every environment.
   2. If `environment.IsDevelopment()`, return.
   3. If `settings.ConnectionString` is not null or whitespace, throw `ConnectionStringNotAllowed`.
   4. If `settings.ServiceUri` is not absolute, its scheme is not `Uri.UriSchemeHttps`, its `Query` is not empty, or its `UserInfo` is not empty, throw `ServiceUriNotAllowed`.
5. `Storage/TableStorageTableSet.cs`: `public sealed partial class TableStorageTableSet`.
   - Constructor `TableStorageTableSet(IEnumerable<string> names)` validates every name and throws `ArgumentException` (parameter name `names`) with:
     - `InvalidTableName` unless the name matches the `[GeneratedRegex("^[A-Za-z][A-Za-z0-9]{2,62}\\z")]` pattern. The anchor must be `\z`, never `$`: in .NET `$` also matches before a final `\n`, so `"Secrets\n"` passed validation. This was a bug, fixed with the regression tests `Constructor_NameWithTrailingLineBreak_Throws` and `Constructor_NameWithTrailingLineBreakAtMaximumLength_Throws` (section 7.2).
     - `ReservedTableName` if the name equals `tables` (OrdinalIgnoreCase).
     - `DuplicateTableName` if two names are equal (OrdinalIgnoreCase).
   - It keeps the input order in `public IReadOnlyList<string> Names { get; }`.
6. `Storage/TableStorageInitializer.cs`: `public sealed partial class TableStorageInitializer(TableServiceClient tableServiceClient, TableStorageTableSet tableSet, ILogger<TableStorageInitializer> logger) : IHostedService`.
   - `StartAsync(CancellationToken cancellationToken)`: for each name in `tableSet.Names`, `await tableServiceClient.CreateTableIfNotExistsAsync(name, cancellationToken)`. Then log through `[LoggerMessage(Level = LogLevel.Information, Message = "Table Storage initialised with {TableCount} tables")] private static partial void LogTablesInitialised(ILogger logger, int tableCount);`. Exceptions propagate, so startup fails fast.
   - `StopAsync` returns `Task.CompletedTask`.
7. `DependencyInjection.cs`: `public static class DependencyInjection` with `public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)` (D6):

   ```csharp
   builder.AddAzureTableServiceClient(
       TableStorageConnection.Name,
       settings => ConfigureTableStorageSettings(settings, builder.Environment),
       clientBuilder => clientBuilder.ConfigureOptions(AddRegisteredPipelinePolicies));
   builder.Services.AddSingleton(new TableStorageTableSet(TableNames.All));
   builder.Services.AddHostedService<TableStorageInitializer>();
   builder.Services.TryAddSingleton(TimeProvider.System);
   return builder;
   ```

   - `private static void ConfigureTableStorageSettings(AzureDataTablesSettings settings, IHostEnvironment environment)` calls `TableStorageSettingsGuard.EnsureAllowed(settings, environment);` and then `settings.Credential ??= new DefaultAzureCredential();`.
   - `private static void AddRegisteredPipelinePolicies(TableClientOptions options, IServiceProvider services)` calls `options.AddPolicy(policy, HttpPipelinePosition.PerCall)` for each `HttpPipelinePolicy` in `services.GetServices<HttpPipelinePolicy>()`. Production registers none. Integration tests register `StorageRequestRecorder`.
   - Verified:
     - Aspire calls `configureSettings` while the client is being registered, so the guard stops startup before the app starts.
     - An https value in `ConnectionStrings:tables` becomes `ServiceUri`.
     - The `ConfigureOptions((options, provider) => ...)` overload exists and the policy sees every logical call.

**Acceptance**

- The build is clean.
- No store code reads `IHostEnvironment`. Only the guard does.
- No message or log contains a configured connection value.

---

### T4. ServiceDefaults: telemetry, health, security headers, Kestrel limits (parallel with T2, T3, T5, T7, T9)

Namespaces are `SharpVault.ServiceDefaults` and `SharpVault.ServiceDefaults.Security`. This covers the Aspire ServiceDefaults template, which is not installed here, with SharpVault changes.

1. `HealthEndpoints.cs`: `public static class HealthEndpoints` with the constants `HealthPath = "/health"`, `AlivePath = "/alive"`, `LiveTag = "live"`, `SelfCheckName = "self"`, `CachePolicyName = "health"` and `public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);`.
2. `RequestLimits.cs`: `public static class RequestLimits { public const long MaxRequestBodyBytes = 1_048_576; }`.
3. `ServiceDefaultsExtensions.cs`: `public static class ServiceDefaultsExtensions` with:
   - `AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder`. It calls, in order:
     1. `ConfigureOpenTelemetry`
     2. `AddDefaultHealthChecks`
     3. `builder.Services.AddServiceDiscovery()`
     4. `builder.Services.ConfigureHttpClientDefaults(http => { http.AddStandardResilienceHandler(); http.AddServiceDiscovery(); })`
     5. `builder.Services.AddOutputCache(options => options.AddPolicy(HealthEndpoints.CachePolicyName, policy => policy.Expire(HealthEndpoints.CacheDuration).SetVaryByQuery([])))`. The empty `SetVaryByQuery` stops cache bypass through random query strings (verified).
     6. `builder.Services.Configure<KestrelServerOptions>(options => { options.AddServerHeader = false; options.Limits.MaxRequestBodySize = RequestLimits.MaxRequestBodyBytes; })`
     7. `return builder;`
   - `ConfigureOpenTelemetry<TBuilder>`:
     - `builder.Logging.AddOpenTelemetry(logging => { logging.IncludeFormattedMessage = true; logging.IncludeScopes = true; })`. This needs `using Microsoft.Extensions.Logging;` (verified).
     - Metrics: AspNetCore, HttpClient and Runtime instrumentation.
     - Tracing: `AddSource(builder.Environment.ApplicationName)`, AspNetCore instrumentation with a `Filter` that excludes `HealthEndpoints.HealthPath` and `HealthEndpoints.AlivePath` (`StartsWithSegments`, ordinal), and HttpClient instrumentation.
     - Call `UseOtlpExporter()` only when `builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]` is not null or whitespace. Keep that key in a `private const string`.
     - Do not enable any option that records request or response bodies or headers.
   - `AddDefaultHealthChecks<TBuilder>`: `builder.Services.AddHealthChecks().AddCheck(HealthEndpoints.SelfCheckName, () => HealthCheckResult.Healthy(), [HealthEndpoints.LiveTag]);`
   - `MapDefaultEndpoints(this WebApplication app)`. Both endpoints are mapped in **every** environment (D8). This was verified to compile.

     ```csharp
     app.MapHealthChecks(HealthEndpoints.HealthPath)
         .AllowAnonymous()
         .CacheOutput(HealthEndpoints.CachePolicyName)
         .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));
     app.MapHealthChecks(HealthEndpoints.AlivePath, new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(HealthEndpoints.LiveTag) })
         .AllowAnonymous()
         .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));
     return app;
     ```

     Do not set a custom `ResponseWriter`. The default writes only `Healthy`, `Degraded` or `Unhealthy`, and sends `Cache-Control: no-store, no-cache` (verified).
4. `Security/SecurityHeaderNames.cs`: `public static class` with these `const string` values: `ContentSecurityPolicy = "Content-Security-Policy"`, `XContentTypeOptions = "X-Content-Type-Options"`, `XFrameOptions = "X-Frame-Options"`, `ReferrerPolicy = "Referrer-Policy"`, `StrictTransportSecurity = "Strict-Transport-Security"`, `PermissionsPolicy = "Permissions-Policy"`, `CrossOriginOpenerPolicy = "Cross-Origin-Opener-Policy"`, `CrossOriginResourcePolicy = "Cross-Origin-Resource-Policy"`, `CacheControl = "Cache-Control"`.
5. `Security/SecurityHeaderValues.cs`: `public static class` with these `const string` values (exact text, tests compare it):
   - `ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"`
   - `WebContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; upgrade-insecure-requests"`
   - `NoSniff = "nosniff"`, `FrameDeny = "DENY"`, `NoReferrer = "no-referrer"`
   - `StrictTransportSecurity = "max-age=31536000; includeSubDomains"`
   - `PermissionsPolicy = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()"`
   - `SameOrigin = "same-origin"`, `NoStore = "no-store"`, `ApiReferencePath = "/scalar"`
6. `Security/SecurityHeadersPolicy.cs`: `public sealed record SecurityHeadersPolicy(string ContentSecurityPolicy, IReadOnlyList<PathString> PathsWithoutContentSecurityPolicy)` with:
   - `public static SecurityHeadersPolicy ForApi(bool allowApiReferenceUi)`: the API CSP. If the flag is true, the exempt list is `[new PathString(SecurityHeaderValues.ApiReferencePath)]`. Otherwise it is empty.
   - `public static SecurityHeadersPolicy ForWeb()`: the web CSP and an empty exempt list.
7. `Security/SecurityHeadersMiddleware.cs`: `public sealed class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersPolicy policy)`.
   - `public Task InvokeAsync(HttpContext context)` registers `context.Response.OnStarting(ApplySecurityHeaders, context)` and returns `next(context)`.
   - `ApplySecurityHeaders(object state)` casts the state to `HttpContext` and **overwrites**, with the indexer: X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Strict-Transport-Security, Permissions-Policy, Cross-Origin-Opener-Policy and Cross-Origin-Resource-Policy.
   - It sets Content-Security-Policy unless `Request.Path.StartsWithSegments` matches any exempt path.
   - It sets `Cache-Control: no-store` only if `Cache-Control` is not already present.
   - It returns `Task.CompletedTask`.
   - Using `OnStarting` means the headers also reach error responses written by the exception handler (verified).
8. `Security/SecurityHeadersExtensions.cs`: `public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, SecurityHeadersPolicy policy) => app.UseMiddleware<SecurityHeadersMiddleware>(policy);`

**Acceptance**

- The build is clean.
- No HTTP logging (`AddHttpLogging`, `UseHttpLogging`) is added anywhere.

---

### T5. UI.Shared shell (parallel with T2, T3, T4, T7, T9)

Follow the `sharpvault-blazor-ui` skill. This project must stay usable by MAUI, so it may not reference `Microsoft.AspNetCore.App`, `HttpContext` or any server type.

**Files** (namespace `SharpVault.UI.Shared`):

- `_Imports.razor`: the `@using` lines for `System.Net.Http`, `Microsoft.AspNetCore.Components.Routing`, `Microsoft.AspNetCore.Components.Web`, `static Microsoft.AspNetCore.Components.Web.RenderMode`, `Microsoft.JSInterop`, `SharpVault.UI.Shared.Layout` and `SharpVault.UI.Shared.Pages`.
- `Layout/MainLayout.razor`: `@inherits LayoutComponentBase`, then `<div id="app-shell" class="app-shell">@Body</div>`, then the Blazor error UI `<div id="blazor-error-ui" data-nosnippet>`. The error UI contains the text "An unhandled error has occurred.", `<a id="blazor-error-reload" href="." class="reload">Reload</a>` and `<span id="blazor-error-dismiss" class="dismiss">x</span>`.
- `Layout/MainLayout.razor.css`: layout styles only, using the CSS variables.
- `Pages/Home.razor`: `@page "/"`, `<PageTitle>SharpVault</PageTitle>`, `<section id="home" class="home"><h1 id="home-heading">SharpVault</h1><p id="home-tagline">Password and secrets manager for your organisation.</p></section>`.
- `Pages/NotFound.razor`: `@page "/not-found"`, `<h1 id="not-found-heading">Not found</h1><p id="not-found-message">The page you are looking for does not exist.</p>`.
- `wwwroot/css/app.css`: CSS custom properties only (`--color-*`, `--space-*`, `--radius-*`), base resets and `#blazor-error-ui` styles (hidden by default). No external font imports (D20). No inline styles anywhere.
- Delete any template leftovers (`Component1.razor`, `ExampleJsInterop.cs`, `exampleJsInterop.js`, `background.png`) if the template was used.

**Acceptance**

- The build is clean.
- No `<script>` or `style=` attribute appears in any `.razor` file of this project.

---

### T6. Api host (after T2, T3, T4)

**Files** (namespace `SharpVault.Api...`):

1. `Security/ApiAuthenticationExtensions.cs`: `public static class ApiAuthenticationExtensions` with `public static IServiceCollection AddApiAuthentication(this IServiceCollection services)`:

   ```csharp
   services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
   services.AddAuthorizationBuilder()
       .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
   return services;
   ```

   The placeholder JwtBearer has no authority and no signing keys. A request without a token gets 401 with `WWW-Authenticate: Bearer`, and an alg-none token also gets 401 (verified, D4). `feature/entra-sign-in` replaces `.AddJwtBearer()` with Microsoft.Identity.Web and adds `Policies`.
2. `OpenApi/ApiDocumentationExtensions.cs`: `public static class ApiDocumentationExtensions` with:
   - `AddApiDocumentation(this IServiceCollection services)`: `services.AddOpenApi(); return services;`
   - `MapApiDocumentation(this WebApplication app)`: only when `app.Environment.IsDevelopment()`, call `app.MapOpenApi().AllowAnonymous();` and `app.MapScalarApiReference().AllowAnonymous();`. Return `app`.
3. `Program.cs` (top-level statements, exactly this order):

   ```csharp
   var builder = WebApplication.CreateBuilder(args);

   builder.AddServiceDefaults();
   builder.Services.AddApplication();
   builder.AddInfrastructure();
   builder.Services.AddProblemDetails();
   builder.Services.AddApiAuthentication();
   builder.Services.AddApiDocumentation();

   var app = builder.Build();

   app.UseSecurityHeaders(SecurityHeadersPolicy.ForApi(app.Environment.IsDevelopment()));
   app.UseExceptionHandler();
   app.UseStatusCodePages();
   app.UseAuthentication();
   app.UseAuthorization();
   app.UseOutputCache();

   app.MapApiDocumentation();
   app.MapDefaultEndpoints();

   await app.RunAsync();
   ```

   - Do not call `UseHttpsRedirection`, `UseHsts`, `UseDeveloperExceptionPage`, `UseCors` or `UseHttpLogging` (D9, D10).
   - `UseExceptionHandler()` runs in every environment. It returns `application/problem+json` without exception details, even in Development (verified).
   - Future endpoint groups are mapped before `MapDefaultEndpoints()`.
   - The `feature/entra-sign-in` lock check middleware goes directly after `UseAuthorization()`.
4. `appsettings.json`: only a `Logging` section (Default `Information`, `Microsoft.AspNetCore` `Warning`) and `"AllowedHosts": "*"`. `appsettings.Development.json`: only `Logging`. **No connection strings** in either file.
5. `Properties/launchSettings.json`: profiles `https` (`https://localhost:7180;http://localhost:5180`) and `http` (`http://localhost:5180`), both with `ASPNETCORE_ENVIRONMENT=Development` and `launchBrowser` false.

**Acceptance**

- The build is clean.
- With the AppHost running (T7), `GET /health` returns `Healthy`, and `/scalar/v1` returns 200 in Development.

---

### T7. AppHost (parallel with T2, T3, T4, T5, T9; the full run check after T6 and T8)

**Files** (namespace `SharpVault.AppHost`):

1. `AppHostResourceNames.cs`: `public static class` with the constants `Storage = "storage"`, `Tables = "tables"`, `Api = "api"`, `Web = "web"` and `HealthPath = "/health"`.
2. `AppHost.cs`:

   ```csharp
   var builder = DistributedApplication.CreateBuilder(args);

   var tables = builder.AddAzureStorage(AppHostResourceNames.Storage)
       .RunAsEmulator(emulator => emulator
           .WithDataVolume()
           .WithLifetime(ContainerLifetime.Persistent))
       .AddTables(AppHostResourceNames.Tables);

   var api = builder.AddProject<Projects.SharpVault_Api>(AppHostResourceNames.Api)
       .WithReference(tables)
       .WaitFor(tables)
       .WithHttpHealthCheck(AppHostResourceNames.HealthPath);

   builder.AddProject<Projects.SharpVault_Web>(AppHostResourceNames.Web)
       .WithExternalHttpEndpoints()
       .WithReference(api)
       .WaitFor(api)
       .WithHttpHealthCheck(AppHostResourceNames.HealthPath);

   await builder.Build().RunAsync();
   ```

3. `Properties/launchSettings.json`:
   - Profile `https`: `applicationUrl` `https://localhost:17260;http://localhost:15260`, with environment `ASPNETCORE_ENVIRONMENT=Development`, `DOTNET_ENVIRONMENT=Development`, `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=https://localhost:21260` and `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=https://localhost:22260`.
   - Profile `http`: the same with http URLs and ports 15260, 19260 and 20260, plus `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`.

**Acceptance**

- The build is clean, with ASPIRE010 suppressed (D15).
- `dotnet run --project src/SharpVault.AppHost --launch-profile http` starts the dashboard, and `docker ps` shows `mcr.microsoft.com/azure-storage/azurite:3.35.0`. Stop the AppHost and the container keeps running, because it is persistent. Start the AppHost again and the same container and volume are reused.
- Clean up afterwards with `docker rm -f` on the container and `docker volume rm` on the `*-storage-data` volume.

---

### T8. Web host (after T4 and T5)

**Files** (namespace `SharpVault.Web`):

1. `Components/App.razor`: the .NET 10 empty template App with these changes:
   - **Remove `<ImportMap />`**, because it renders an inline script that the CSP blocks (D21).
   - The stylesheets are `@Assets["_content/SharpVault.UI.Shared/css/app.css"]` and `@Assets["SharpVault.Web.styles.css"]`.
   - The body is `<Routes />`, `<ReconnectModal />` and `<script src="@Assets["_framework/blazor.web.js"]"></script>`. Keep `<ResourcePreloader />` and `<HeadOutlet />`.
2. `Components/Routes.razor`: `<Router AppAssembly="typeof(MainLayout).Assembly" AdditionalAssemblies="new[] { typeof(Program).Assembly }" NotFoundPage="typeof(NotFound)">` with the template's `Found` block (`RouteView` with `DefaultLayout="typeof(MainLayout)"` and `FocusOnNavigate Selector="h1"`).
3. `Components/_Imports.razor`: the UI.Shared usings, plus `SharpVault.Web.Components` and `SharpVault.Web.Components.Layout`.
4. `Components/Pages/Error.razor`: `@page "/error"`. It contains `<h1 id="error-heading">Something went wrong</h1>`, `<p id="error-message">An error occurred while processing your request.</p>` and, when a request id exists, `<p id="error-request-id">Request ID: <code>@RequestId</code></p>`. Keep the template's `HttpContext` cascading parameter and the `Activity.Current?.Id ?? HttpContext?.TraceIdentifier` logic. Remove the template's "Development Mode" text.
5. `Components/Layout/ReconnectModal.razor`, `.razor.css` and `.razor.js`: copied from the template with every comment removed, including in the JS.
6. `WebRoutes.cs`: `public static class WebRoutes { public const string Error = "/error"; public const string NotFound = "/not-found"; }`
7. `Program.cs`:

   ```csharp
   var builder = WebApplication.CreateBuilder(args);

   builder.AddServiceDefaults();
   builder.Services.AddRazorComponents().AddInteractiveServerComponents();

   var app = builder.Build();

   app.UseSecurityHeaders(SecurityHeadersPolicy.ForWeb());
   app.UseExceptionHandler(WebRoutes.Error, createScopeForErrors: true);
   app.UseStatusCodePagesWithReExecute(WebRoutes.NotFound, createScopeForStatusCodePages: true);
   app.UseAntiforgery();
   app.UseOutputCache();

   app.MapStaticAssets();
   app.MapRazorComponents<App>()
       .AddInteractiveServerRenderMode()
       .AddAdditionalAssemblies(typeof(MainLayout).Assembly);
   app.MapDefaultEndpoints();

   await app.RunAsync();
   ```

   No authentication or authorization yet (D5). No `UseHttpsRedirection` or `UseHsts`, because the middleware already emits HSTS (D9).
8. `appsettings.json`, `appsettings.Development.json` and `Properties/launchSettings.json`: as in T6, with ports 7190 and 5190.

**Acceptance**

- The build is clean.
- `GET /` renders `id="home-heading"`.
- The page source has no `<script>` element without a `src` attribute.

---

### T9. CI, CodeQL, dependency review, Dependabot and the coverage gate (parallel after T1)

1. `eng/CoverageGate.cs`: a .NET 10 file-based app, run with `dotnet run eng/CoverageGate.cs -- <cobertura.xml> <Prefix>=<Percent> ...`. Top-level statements, AGPL header, no comments.
   - Constants: `ExitSuccess = 0`, `ExitBelowThreshold = 1`, `ExitUsageError = 2`.
   - Missing arguments, a missing file or a malformed `Prefix=Percent` pair prints usage and exits `2`.
   - Load the file with `XDocument`. For each threshold, select every `class` element whose `name` equals the prefix or starts with `prefix + "."`.
   - Collect their `line` elements, distinct by (`filename` of the class, `number`). A line counts as covered if any `hits` is greater than 0.
   - Zero coverable lines prints `"{prefix}: no coverable lines found"` and counts as a failure. A missing assembly must never pass silently.
   - Print `"{prefix}: {percent:F1}% line coverage (threshold {threshold}%)"` for each prefix, using `CultureInfo.InvariantCulture`.
   - Exit `1` if any prefix is below its threshold. File-based apps pick up `Directory.Build.props` (verified).
2. `.github/workflows/ci.yml`
   - `on`: `pull_request` and `push` to `main`. `permissions: contents: read`. A concurrency group per ref with `cancel-in-progress: true`. Environment `DOTNET_NOLOGO=true` and `DOTNET_CLI_TELEMETRY_OPTOUT=true`.
   - Job `build-and-test` (`ubuntu-latest`, `timeout-minutes: 30`) steps:
     1. `actions/checkout@v5`
     2. `actions/setup-dotnet@v5` with `dotnet-version: 10.0.x`
     3. `dotnet --info`
     4. `docker info`
     5. `dotnet tool restore`
     6. `dotnet restore SharpVault.slnx`
     7. "Vulnerable packages": `dotnet list SharpVault.slnx package --vulnerable --include-transitive 2>&1 | tee vulnerable.txt`, then `if grep -qi "has the following vulnerable packages" vulnerable.txt; then exit 1; fi`
     8. `dotnet build SharpVault.slnx -c Release --no-restore`
     9. `dotnet test SharpVault.slnx -c Release --no-build --filter "Category!=Performance" --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults --logger trx`
     10. `dotnet reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" "-targetdir:TestResults/coverage" "-reporttypes:Cobertura;MarkdownSummaryGithub"`
     11. `cat TestResults/coverage/SummaryGithub.md >> "$GITHUB_STEP_SUMMARY"`
     12. `dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80`
     13. `actions/upload-artifact@v4` with `if: always()`, name `test-results` and path `TestResults`.
   - Job `performance`: `if: github.event_name == 'push' && github.ref == 'refs/heads/main'`, `needs: build-and-test`. Steps are checkout, setup-dotnet and `dotnet test SharpVault.slnx -c Release --filter "Category=Performance"`.
3. `.github/workflows/codeql.yml`
   - `on`: `pull_request` and `push` to `main`, plus `schedule` `cron: '0 3 * * 1'`.
   - Permissions: `contents: read`, `security-events: write`, `actions: read`.
   - Steps: checkout, setup-dotnet 10.0.x, `github/codeql-action/init@v4` (`languages: csharp`, `build-mode: manual`, `queries: security-extended`), `dotnet build SharpVault.slnx -c Release`, `github/codeql-action/analyze@v4`.
4. `.github/workflows/dependency-review.yml`: `on: pull_request` to `main`, `permissions: contents: read`, steps checkout and `actions/dependency-review-action@v4` with `fail-on-severity: low`.
5. `.github/dependabot.yml`: version 2, weekly.
   - `nuget` at `/`, with groups `aspire` (`Aspire.*`), `opentelemetry` (`OpenTelemetry.*`), `microsoft` (`Microsoft.*`, `System.*`) and `xunit` (`xunit*`).
   - `github-actions` at `/`.

**Acceptance**

- The YAML parses (`python3 -c "import yaml,sys; [yaml.safe_load(open(f)) for f in sys.argv[1:]]" .github/workflows/*.yml .github/dependabot.yml`).
- Running the CI commands of steps 5 to 12 locally passes once T10 to T13 are in.
- `dotnet run eng/CoverageGate.cs -- <merged report> SharpVault.Application=100.1` exits 1. `SharpVault.DoesNotExist=1` exits 1. No arguments exits 2.

---

### T10. Integration test infrastructure (test-writer, after T3 and T6)

This is the reusable skeleton that later features build on. Namespace `SharpVault.IntegrationTests...`. Every class is `sealed` unless it is a base class.

1. `Factories/TestContainerImages.cs`: `public const string Azurite = "mcr.microsoft.com/azure-storage/azurite:3.35.0";`
2. `Factories/ApiTestFactory.cs`: `public sealed class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime`.
   - Field: `AzuriteContainer _azurite = new AzuriteBuilder(TestContainerImages.Azurite).WithInMemoryPersistence().Build();`. Use the string constructor; the parameterless one is not used.
   - Properties:
     - `public StorageRequestRecorder StorageRequests { get; } = new();`
     - `public CapturingLoggerProvider Logs { get; } = new();`
     - `public TableServiceClient StorageAdminClient`: returns a client created in `InitializeAsync` from the container connection string, or throws `InvalidOperationException` if the factory has not been initialised. No `!` operator. This client is not instrumented, so setup and reset calls are never recorded.
   - `InitializeAsync()`: starts the container and creates `StorageAdminClient`.
   - `DisposeAsync()` (override): `await base.DisposeAsync(); await _azurite.DisposeAsync();`
   - `ConfigureWebHost`:
     - `UseEnvironment(Environments.Development)`
     - `UseSetting($"ConnectionStrings:{TableStorageConnection.Name}", _azurite.GetConnectionString())`. Use `UseSetting`, not `ConfigureAppConfiguration`, because Aspire reads the value early (verified).
     - `UseSetting("Logging:LogLevel:Default", "Debug")`
     - `ConfigureLogging(logging => logging.AddProvider(Logs))`
     - `ConfigureTestServices`: `services.AddSingleton<HttpPipelinePolicy>(StorageRequests);` and `services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });`
   - Methods:
     - `CreateUnauthenticatedClient()`: `CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false })`
     - `CreateAuthenticatedClient(TestUser user)`: an unauthenticated client plus `TestAuthenticationHandler` headers.
     - `ResetStorageAsync(CancellationToken cancellationToken)`: list every table through `StorageAdminClient.QueryAsync`, delete each one, then call `CreateTableIfNotExistsAsync` for every name in `Services.GetRequiredService<TableStorageTableSet>().Names`.
3. `Factories/ProductionApiTestFactory.cs`: `public sealed class ProductionApiTestFactory : WebApplicationFactory<Program>`. No container.
   - `public const string ServiceUri = "https://sharpvaultfoundationtest.table.core.windows.net/";`
   - Environment `Production`, `ConnectionStrings:tables` = `ServiceUri`, logging into its own `Logs` capture provider.
   - In `ConfigureTestServices`, remove the `IHostedService` descriptor whose `ImplementationType` is `TableStorageInitializer`, so no Entra token is requested.
   - Uses the real placeholder JwtBearer scheme, not the test handler.
   - Tests on this factory never call `/health`, because that would try `DefaultAzureCredential` (D27).
4. `Factories/WebTestFactory.cs`: `extern alias WebApp;` as the first line after the header. `public sealed class WebTestFactory : WebApplicationFactory<WebApp::Program>` with environment Development and `CreateWebClient()` (https://localhost, no automatic redirects).
5. `Factories/StorageTestGroup.cs`, `ProductionTestGroup.cs` and `WebTestGroup.cs`: `[CollectionDefinition(nameof(X))] public sealed class X : ICollectionFixture<TFactory>;`. The names end in `Group`, not `Collection`, because CA1711 forbids the `Collection` suffix on these types (D16). Every test class has `[Collection(...)]`.
6. `Authentication/TestAuthenticationHandler.cs`: `public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)`.
   - Constants: `SchemeName = "Test"`, `ObjectIdHeader = "X-Test-Oid"`, `NameHeader = "X-Test-Name"`, `TenantHeader = "X-Test-Tid"`, `RolesHeader = "X-Test-Roles"` (comma separated).
   - With no `X-Test-Oid` header it returns `AuthenticateResult.NoResult()`.
   - Otherwise it builds a `ClaimsIdentity(SchemeName, "name", "roles")` with the claims `oid`, `tid`, `name` and one `roles` claim per role.
   - `public static void AddHeaders(HttpClient client, TestUser user)` writes the headers.
7. `Authentication/TestUser.cs`: `public sealed record TestUser(Guid ObjectId, string DisplayName, Guid TenantId, IReadOnlyList<string> Roles);`
8. `Authentication/TestTenant.cs`: `public static readonly Guid Id = new("5f0c1d2e-4b7a-4c3d-9e8f-00000000a001");`
9. `Authentication/TestUsers.cs`: `public static TestUser AuthenticatedWithoutRole { get; } = new(new Guid("5f0c1d2e-4b7a-4c3d-9e8f-00000000b001"), "Test user without role", TestTenant.Id, []);`. `feature/entra-sign-in` adds Owner, OtherUser, Admin, LockedUser, Guest and OtherTenantUser here.
10. `Authentication/ForgedTokens.cs`: `public static class` with:
    - `AlgNone()`: base64url of `{"alg":"none","typ":"JWT"}`, a dot, base64url of the payload, and a trailing dot.
    - `SignedWithUnknownKey()`: HS256 using a 32-byte key from `RandomNumberGenerator.GetBytes(32)`, `HMACSHA256.HashData` and `System.Buffers.Text.Base64Url`.
    - The payload is built with `System.Text.Json`: `oid`, `tid` = `TestTenant.Id`, `aud` = `api://sharpvault`, `iss` = `https://login.microsoftonline.com/{tid}/v2.0`, `nbf` = now, `exp` = now + 1 hour (Unix seconds).
    - No new packages.
11. `Storage/RecordedStorageRequest.cs`: `public sealed record RecordedStorageRequest(string Method, string Path, string? Filter)` with:
    - `bool IsEntityQuery`: `Method == "GET"` and the last path segment ends with `()`.
    - `bool HasPartitionKeyCondition`: `Filter` contains `PartitionKey eq` (ordinal).
12. `Storage/StorageRequestRecorder.cs`: `public sealed class StorageRequestRecorder : HttpPipelinePolicy`.
    - A `ConcurrentQueue<RecordedStorageRequest>`, populated by `Process` and `ProcessAsync` before calling `ProcessNext` or `ProcessNextAsync`.
    - The path comes from `message.Request.Uri.ToUri().AbsolutePath`. The filter is the `$filter` value from `QueryHelpers.ParseQuery`.
    - Members: `Requests`, `Count`, `Clear()` and `IReadOnlyList<RecordedStorageRequest> QueriesWithoutPartitionKey()` (entity queries without a partition key condition).
13. `Logging/CapturedLogEntry.cs` (record: Category, Level, Message, Exception text) and `Logging/CapturingLoggerProvider.cs`.
    - The provider is an `ILoggerProvider` with a private nested logger that captures every level into a `ConcurrentQueue`.
    - Members: `Entries`, `Clear()` and `bool ContainsText(string value)`, which searches both the message and the exception text (ordinal).
14. `Support/SensitiveMarkers.cs`: the constants `ExceptionMessage = "SV-SENSITIVE-EXCEPTION-7f3c9a"` and `HealthCheckMessage = "SV-SENSITIVE-HEALTH-41d2e8"`.
15. `Support/ThrowingHealthCheck.cs`: an `IHealthCheck` that throws `InvalidOperationException(SensitiveMarkers.HealthCheckMessage)`.
16. `Support/ThrowingRequestStartupFilter.cs`: an `IStartupFilter` whose `Configure` calls `next(app)` and then `app.Map("/test/throw", branch => branch.Run(_ => throw new InvalidOperationException(SensitiveMarkers.ExceptionMessage)))`. Use it only for the Web, which has no fallback policy. On the API, unmatched requests hit the fallback policy first (verified).
17. `ApiTests/BaseIntegrationTest.cs`: `public abstract class BaseIntegrationTest(ApiTestFactory factory) : IAsyncLifetime`.
    - Members: `Factory`, `Fixture` (AutoFixture), `HttpClient Client` (starts unauthenticated), `SetAuthenticatedClient(TestUser user)`, `CreateUnauthenticatedClient()`, `StorageRequests`, `Logs`, `GetTableClient(string tableName)` (through `StorageAdminClient`) and `AssertLogsDoNotContain(params string[] values)`.
    - `InitializeAsync` clears the recorder and the logs.
    - `DisposeAsync` awaits `Factory.ResetStorageAsync(CancellationToken.None)` and calls `GC.SuppressFinalize(this)`.

Rules for every test file:

- Pass `TestContext.Current.CancellationToken` to every call that accepts a token (xUnit1051 is a warning, so it fails the build).
- Never call `services.AddOpenApi()` in a test project. It triggers the OpenAPI source generator error CS9137. To change OpenAPI behaviour use `services.Configure<OpenApiOptions>("v1", ...)` (verified, D25).
- No `Thread.Sleep` or `Task.Delay` waits.

**Acceptance**

- The infrastructure tests in section 7.3 pass.
- No class in `src/` references anything in this project.

---

### T11. Integration, security and performance tests (test-writer, after T10)

Write every test listed in section 7.3 to 7.6.

### T12. AppHost model tests (test-writer, after T7)

Write the tests in section 7.7, using `DistributedApplicationTestingBuilder.CreateAsync<Projects.SharpVault_AppHost>(TestContext.Current.CancellationToken)`. Inspect the resources without starting them (verified: no Docker or DCP needed).

### T13. Unit and architecture tests (test-writer, after T2, T3, T4)

Write the tests in section 7.1 and 7.2.

### T14. Documentation (docs-writer, after all)

- `README.md`:
  - Getting Started: `dotnet run --project src/SharpVault.AppHost`, Docker required, and `dotnet dev-certs https --trust` for the https profile.
  - The architecture tree: add `SharpVault.AppHost.Tests`, `eng/` and `SharpVault.slnx`, and mark `SharpVault.Maui` as "planned in feature/maui-app".
  - A CI badge.
- `docs/LOCAL-DEV.md` (new):
  - Running the AppHost, the http and https profiles, and the persistent Azurite container and volume.
  - How to wipe local data (`docker rm -f` the container, `docker volume rm` the `*-storage-data` volume).
  - Running tests and filters, the coverage commands, and the note that Maui is not part of the Linux build.
- `docs/STORAGE.md` (new):
  - The `ConnectionStrings:tables` rules: an https URI with `DefaultAzureCredential` everywhere, and connection strings only in Development.
  - The "Storage Table Data Contributor" role.
  - How to add a table (constant in `TableNames`, append to `All`, document the table here).
  - The empty tables list.
- `docs/TESTING.md` (new): the factories and collections, `TestUsers`, `StorageRequestRecorder` (call counts and `QueriesWithoutPartitionKey`), `CapturingLoggerProvider`, `ForgedTokens`, the rules about `TestContext.Current.CancellationToken` and `AddOpenApi`, and the trait filters.
- `docs/PROGRESS.md`:
  - Move the foundation to Done.
  - Add to Next: pin GitHub Actions to commit SHAs (D19), entra-sign-in decides the fallback role requirement (D4), Playwright setup in the first interactive UI feature (D23), and Benchmarks project in encryption-core.

## 6. Threats

Every threat has at least one named test in section 7. Assume each one exists until its test passes. The foundation has these surfaces: API `/health`, `/alive`, `/openapi/{documentName}.json` and `/scalar/*` (Development only), any other API route, the Web `/`, `/not-found`, `/error` and the Web health endpoints, the Table Storage configuration, the AppHost and CI.

| # | Category | Threat | Mitigation | Proving tests |
|---|---|---|---|---|
| TH1 | Access | Anonymous caller reaches an API route that is not a health endpoint, including future endpoints that forget an authorization policy | Fallback policy requires an authenticated user. The inventory test fails any endpoint that is anonymous without being on the allow-list, or has no explicit policy | `UnknownRoute_AnonymousCaller_Returns401`, `Endpoints_EveryRoute_IsAllowListedAnonymousOrHasExplicitPolicy`, `Endpoints_ProductionEnvironment_OnlyHealthEndpointsAreAnonymous`, `FallbackPolicy_Configured_RequiresAuthenticatedUser` |
| TH2 | Access | Forged token (alg `none`, or signed with an attacker key) accepted by the placeholder scheme | JwtBearer with no keys and no authority rejects every token | `UnknownRoute_ProductionAlgNoneToken_Returns401`, `UnknownRoute_ProductionTokenSignedWithUnknownKey_Returns401`, `UnknownRoute_ProductionAnonymous_Returns401WithBearerChallenge` |
| TH3 | Access | Route existence revealed to anonymous callers (404 compared with 401) | Fallback applies to unmatched requests, so anonymous callers get 401 for every route | `UnknownRoute_AnonymousCaller_Returns401`, `UnknownRoute_AuthenticatedCaller_Returns404` |
| TH4 | Escalation | Test authentication handler or a custom handler ships in production | The handler lives only in the test project. An architecture test forbids `AuthenticationHandler<>` subclasses in production assemblies | `ProductionAssemblies_Types_DeclareNoAuthenticationHandler` |
| TH5 | Leakage | API docs (OpenAPI, Scalar) exposed outside Development | Mapped only in Development. Elsewhere the fallback policy returns 401 | `OpenApiDocument_Production_Returns401`, `ApiReference_Production_Returns401`, `OpenApiDocument_Development_Returns200Anonymously`, `ApiReference_Development_Returns200Anonymously` |
| TH6 | Leakage | Health endpoint leaks exception text, storage account or endpoint names | Default writer returns the status word only | `HealthEndpoint_AnonymousCallerWithHealthyStorage_Returns200WithStatusOnlyBody`, `HealthEndpoint_CheckThrowsWithSensitiveMessage_Returns503WithStatusOnlyBody` |
| TH7 | Leakage | Stack traces or exception messages in API error responses, including in Development | `UseExceptionHandler()` and ProblemDetails in every environment, never the developer exception page | `UnhandledException_Development_ReturnsProblemDetailsWithoutDetails`, `UnknownRoute_AnonymousCaller_ReturnsProblemDetailsBody` |
| TH8 | Leakage | Stack traces on the Web error page | Exception handler in every environment, and a generic Error page | `UnhandledException_Web_RendersErrorPageWithoutExceptionDetails` |
| TH9 | Leakage | Bearer tokens or cookies written to logs | No HTTP logging. IdentityModel PII is hidden by default. Tested at Debug level | `UnknownRoute_BearerTokenAndCookieSent_NeitherIsLogged`, `UnknownRoute_ProductionForgedToken_TokenNotLogged` |
| TH10 | Leakage | Startup errors echo the connection string or account key | Guard messages are constants without values | `EnsureAllowed_ProductionWithAccountKeyConnectionString_ThrowsWithoutEchoingValue`, `Startup_ProductionWithAccountKeyConnectionString_FailsWithoutEchoingKey` |
| TH11 | Leakage | Responses cached by browsers or proxies | `Cache-Control: no-store` when the endpoint sets none. Health sends `no-store, no-cache` | `ApiResponse_WithoutExplicitCacheControl_SendsNoStore`, `HealthEndpoint_AnyCall_SendsNoStoreCacheControl`, `HomePage_Response_SendsNoStore` |
| TH12 | Tampering / config | Account key, SAS or Azurite connection used outside Development, bypassing managed identity | Guard in Aspire `configureSettings` rejects connection strings, http, query strings and user info outside Development | the 13 guard unit tests in 7.2, `Startup_ProductionWithAccountKeyConnectionString_FailsWithoutEchoingKey`, `Startup_ProductionWithSasServiceUri_Fails` |
| TH13 | Tampering | Clickjacking, MIME sniffing, downgrade, referrer leaks | Security headers middleware on every response, including errors | `ApiResponse_AnyRoute_HasSecurityHeaders`, `ApiResponse_Production_HasStrictContentSecurityPolicy`, `HomePage_Response_HasWebSecurityHeaders`, `UnknownRoute_AnonymousCaller_ErrorResponseHasSecurityHeaders` |
| TH14 | Tampering | XSS through inline script in the Web shell (needed later for OTL-3) | Strict CSP without `unsafe-inline` or `unsafe-eval`, and no `<ImportMap />` | `HomePage_Rendered_ContainsNoInlineScript`, `ForWeb_Always_DisallowsInlineAndEvalScript` |
| TH15 | Tampering | Cross-origin browser calls to the API | No CORS policy, so no `Access-Control-Allow-Origin` | `CorsPreflight_UntrustedOrigin_ReturnsNoAllowOriginHeader` |
| TH16 | Denial of service | Anonymous caller forces a storage call per request through `/health` (repeat, random query string, random `Host` header, both together, POST) | Output cache for 5 s that varies by neither the query nor the `Host` header (`SetVaryByQuery([])` and `SetVaryByHost(false)`; the output cache varies by host by default), GET/HEAD only (POST gets 401, verified), `/alive` never touches storage | `HealthEndpoint_TwentyRequestsWithinCacheWindow_MakesOneStorageCall`, `HealthEndpoint_RandomQueryStrings_MakesOneStorageCall`, `HealthEndpoint_RandomHostHeaders_MakesOneStorageCall`, `HealthEndpoint_RandomHostHeadersAndQueryStrings_MakesOneStorageCall`, `HealthEndpoint_PostRequest_Returns401WithoutStorageCall`, `AliveEndpoint_AnonymousCaller_Returns200WithoutStorageCall` |
| TH17 | Denial of service | Huge request bodies | Kestrel `MaxRequestBodySize` is 1 MiB | `AddServiceDefaults_Registered_LimitsRequestBodyToOneMebibyte` |
| TH18 | Denial of service | Table creation per request, or table scans | Initializer creates tables once at start. The recorder detects queries without a partition key | `Startup_TableSetConfigured_MakesOneStorageCallPerTable`, `StorageRequestRecorder_QueryWithoutPartitionKey_IsReported`, `StorageRequestRecorder_QueryWithPartitionKey_IsNotReported` |
| TH19 | Denial of service | Liveness flaps when storage is down and the platform restarts instances | `/alive` runs only the `live` check | `AliveEndpoint_StorageCheckFailing_StillReturns200` |
| TH20 | Leakage | Mediator writes notifications to disk | Persistence explicitly disabled | `AddApplication_Registered_DisablesNotificationPersistence` |
| TH21 | Information disclosure | `Server: Kestrel` header | `AddServerHeader = false` | `AddServiceDefaults_Registered_HidesServerHeader` |
| TH22 | Supply chain | Vulnerable or malicious package | NuGet audit (all, low) as errors, CI vulnerable step, dependency review, CodeQL, Dependabot | CI job `build-and-test` step "Vulnerable packages", `dependency-review.yml`, `codeql.yml`. Restore itself fails on NU1901-NU1904 |
| TH23 | Architecture drift | Domain or Application take a storage or web dependency; UI.Shared takes a server dependency, which breaks MAUI | Reflection layering tests | the six `*_ReferencedAssemblies_*` tests in 7.1 |
| TH24 | Local data | Persistent Azurite loses or mixes data, or the AppHost runs a different image than the tests | AppHost model tests check lifetime, volume and image | `AppHost_StorageEmulator_UsesPersistentLifetime`, `AppHost_StorageEmulator_MountsDataVolumeAtData`, `AppHost_StorageEmulator_UsesAzuriteImage` |
| TH25 | Leakage | A secret or token sent in a query string ends up in request or HttpClient logs, or in trace tags (OTL-3, AUD-3). Package defaults are not enough: provider-specific logging configuration (for example `Logging:Console:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics`) or `OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION` turns them back on | `SuppressQueryStringLogging` in ServiceDefaults (called by `AddServiceDefaults`) adds code-level filters at `Warning` for `Microsoft.AspNetCore.Hosting.Diagnostics` and `System.Net.Http.HttpClient`. A `PostConfigure<LoggerFilterOptions>` then hardens them against configuration: it raises every rule for those categories that is below `Warning` to `Warning`, and adds a `Warning` rule for those categories for every provider-specific rule that would otherwise cover them. AspNetCore tracing removes the `url.query` tag from server spans (`EnrichWithHttpRequest`). HttpClient tracing sets outbound `url.full` without the query (`EnrichWithHttpRequestMessage`) | `UnknownRoute_SecretInQueryString_IsNotLogged`, `UnknownRoute_SecretInQueryString_IsNotInTraceTags`, `UnknownRoute_SecretInQueryStringWithPackageRedactionDisabled_IsNotInTraceTags`, `WebHost_SecretInQueryString_IsNotLogged`, `SuppressQueryStringLogging_Registered_FiltersHostingDiagnosticsAndHttpClientBelowWarning`, `SuppressQueryStringLogging_ProviderSpecificConfigLowersLevel_EffectiveLevelStaysWarning` |

## 7. Tests

Conventions:

- Names follow `Scenario_Condition_ExpectedResult`.
- Security tests have `[Trait("Category", "Security")]`, performance tests `[Trait("Category", "Performance")]`.
- Every integration test class has `[Collection(...)]`.
- Assert exact status codes and exact header values. Use xUnit `Assert` only (D18).

### 7.1 Architecture tests: `tests/SharpVault.UnitTests/Architecture/LayeringTests.cs`

- Load the assemblies with `Assembly.Load(new AssemblyName("SharpVault.X"))`. Never reference `Program` (two global `Program` types exist).
- Inspect `GetReferencedAssemblies()`. A rule may pass trivially while a project is empty (Domain), and becomes meaningful as code arrives.

| Test | Rule |
|---|---|
| `Domain_ReferencedAssemblies_ContainNoOtherSharpVaultProject` | no `SharpVault.*` |
| `Domain_ReferencedAssemblies_ContainNoStorageWebOrApplicationFramework` | no `Azure.*`, `Microsoft.AspNetCore.*`, `Mapster*`, `FluentValidation*`, `Mediator` |
| `Application_ReferencedAssemblies_ContainOnlyDomainAmongSharpVaultProjects` | only `SharpVault.Domain` |
| `Application_ReferencedAssemblies_ContainNoAzureSdkOrAspNetCore` | no `Azure.*`, `Microsoft.AspNetCore.*` |
| `Infrastructure_ReferencedAssemblies_ContainNoApiWebOrUiProject` | no `SharpVault.Api`, `SharpVault.Web`, `SharpVault.UI.Shared` |
| `UiShared_ReferencedAssemblies_ContainNoServerSideProjectOrServerFramework` | no `SharpVault.Infrastructure`, `SharpVault.Api`, `SharpVault.Web`, `Azure.*`, `Microsoft.AspNetCore.Http*`, `Microsoft.AspNetCore.Hosting*`, `Microsoft.AspNetCore.Mvc*` |
| `ProductionAssemblies_ConcreteClasses_AreSealed` | every class in a `SharpVault.*` namespace of Domain, Application, Infrastructure, ServiceDefaults, Api, UI.Shared and Web is `sealed`, `abstract` or `static`. Excluded: types with `CompilerGeneratedAttribute` or `GeneratedCodeAttribute`, names containing `<`, and subclasses of `Microsoft.AspNetCore.Components.ComponentBase` (Razor components). One exception is excluded by name: the Razor `_Imports` class, because the Razor compiler emits it without either attribute. It is excluded only when it is named `_Imports` and has the exact generated shape: its declared members are constructors and a method named `Execute`, nothing else (D28). Any other framework-generated type that trips the rule is excluded by attribute, never by name |
| `ProductionAssemblies_Types_DeclareNoAuthenticationHandler` | no type derives from `Microsoft.AspNetCore.Authentication.AuthenticationHandler<>` |
| `ProductionAssemblies_Namespaces_StartWithAssemblyName` | every type in a `SharpVault` namespace has a namespace starting with its assembly name |

### 7.2 Unit tests: `tests/SharpVault.UnitTests`

`Infrastructure/Storage/TableStorageSettingsGuardTests.cs` (Security). Use NSubstitute for `IHostEnvironment` with `EnvironmentName`. The Azurite well-known development connection string is an acceptable test value.

- `EnsureAllowed_DevelopmentWithAzuriteConnectionString_DoesNotThrow`
- `EnsureAllowed_DevelopmentWithHttpsServiceUri_DoesNotThrow`
- `EnsureAllowed_ProductionWithHttpsServiceUri_DoesNotThrow`
- `EnsureAllowed_StagingWithHttpsServiceUri_DoesNotThrow`
- `EnsureAllowed_ProductionWithAccountKeyConnectionString_ThrowsWithoutEchoingValue`: the message equals `ConnectionStringNotAllowed` and contains neither `AccountKey` nor the key value.
- `EnsureAllowed_ProductionWithUseDevelopmentStorage_Throws`
- `EnsureAllowed_ProductionWithConnectionStringAndServiceUri_Throws`
- `EnsureAllowed_ProductionWithSasQueryOnServiceUri_Throws`
- `EnsureAllowed_ProductionWithHttpServiceUri_Throws`
- `EnsureAllowed_ProductionWithUserInfoInServiceUri_Throws`
- `EnsureAllowed_ProductionWithRelativeServiceUri_Throws`
- `EnsureAllowed_NothingConfigured_Throws` (Theory: `Development`, `Production`)
- `EnsureAllowed_WhitespaceConnectionStringAndNoUri_Throws` (Theory: `Development`, `Production`)

`Infrastructure/Storage/TableStorageTableSetTests.cs`

- `Constructor_ValidNames_ExposesNamesInOrder` (AutoFixture-generated valid names, prefixed with a letter and trimmed to 3 to 63 alphanumerics)
- `Constructor_EmptyList_ExposesNoNames`
- `Constructor_NameShorterThanThreeCharacters_Throws`
- `Constructor_NameLongerThanSixtyThreeCharacters_Throws`
- `Constructor_NameOfExactlyThreeAndSixtyThreeCharacters_IsAccepted`
- `Constructor_NameStartingWithDigit_Throws`
- `Constructor_NameWithIllegalCharacter_Throws` (Theory: `Bad-Name`, `Bad Name`, `Bad/Name`, `Bad\Name`, `Bad#Name`, `Bad?Name`, `Bad\u0000Name`, `Bad\tName`, `Tablé1`, `' or PartitionKey ne '`) (Security)
- `Constructor_ReservedNameTables_Throws` (Theory: `tables`, `Tables`, `TABLES`)
- `Constructor_DuplicateNamesDifferingOnlyInCase_Throws`
- `Constructor_NameWithTrailingLineBreak_Throws` (Security, regression for the `$` anchor; Theory: `Secrets\n`, `Secrets\r\n`, `Secrets\n\n`). Throws `ArgumentException` with `InvalidTableName`.
- `Constructor_NameWithTrailingLineBreakAtMaximumLength_Throws` (regression): `"T"` plus 62 `a` plus `\n` throws `ArgumentException`. With `$` this name passed, because the 63 characters before the line break are valid.
- `TableNames_All_PassesTableSetValidation`

`Infrastructure/Storage/TableStorageInitializerTests.cs` (NSubstitute `TableServiceClient`)

- `StartAsync_TableSetWithTwoNames_CreatesEachTableOnce`
- `StartAsync_EmptyTableSet_MakesNoStorageCalls`
- `StartAsync_WithCancellationToken_PassesTokenToEveryCall`
- `StartAsync_StorageThrows_PropagatesException`
- `StopAsync_Always_MakesNoStorageCalls`

`Application/DependencyInjectionTests.cs` (build `new ServiceCollection().AddLogging().AddApplication()`)

- `AddApplication_Registered_ResolvesMediator`
- `AddApplication_Registered_DisablesNotificationPersistence`: `IOptions<Mediator.Core.MediatorOptions>.Value.EnablePersistence` is false, and `Mediator.Persistence.INotificationPersistence` resolves to null (verified behaviour).
- `AddApplication_Registered_ResolvesMapsterMapper`

`ServiceDefaults/SecurityHeadersPolicyTests.cs`

- `ForApi_WithoutApiReferenceUi_UsesApiPolicyWithNoExemptPaths`
- `ForApi_WithApiReferenceUi_ExemptsOnlyScalarPath`
- `ForWeb_Always_DisallowsInlineAndEvalScript`: the CSP does not contain `unsafe-inline` or `unsafe-eval`, and it contains `frame-ancestors 'none'` (Security).
- `ForApi_Always_DeniesEverySourceByDefault`: contains `default-src 'none'`.

`ServiceDefaults/ServiceDefaultsExtensionsTests.cs` (`WebApplication.CreateBuilder()` + `AddServiceDefaults()`, then resolve the options)

- `AddServiceDefaults_Registered_HidesServerHeader` (Security)
- `AddServiceDefaults_Registered_LimitsRequestBodyToOneMebibyte` (Security)
- `AddServiceDefaults_Registered_AddsSelfCheckTaggedLive`
- `SuppressQueryStringLogging_Registered_FiltersHostingDiagnosticsAndHttpClientBelowWarning` (Security; Theory: `Microsoft.AspNetCore.Hosting.Diagnostics`, `System.Net.Http.HttpClient`): `LoggerFilterOptions.Rules` contains a rule with no provider, that category and `LogLevel.Warning`.
- `SuppressQueryStringLogging_ProviderSpecificConfigLowersLevel_EffectiveLevelStaysWarning` (Security; same Theory): in Production, configuration sets `Logging:Console:LogLevel` and `Logging:OpenTelemetry:LogLevel` to `Information` for Hosting.Diagnostics and `Debug` for HttpClient. A logger for the category is not enabled for `Information` or `Debug` and is enabled for `Warning`.

### 7.3 Test infrastructure self-tests: `tests/SharpVault.IntegrationTests/ApiTests/TestInfrastructureTests.cs`

Collection: StorageTestGroup.

- `ResetStorage_AfterTableCreated_RemovesTable`
- `StorageRequestRecorder_QueryWithoutPartitionKey_IsReported`: query a table through the **app's** `TableServiceClient` (from `Factory.Services`) with filter `RowKey eq 'x'`.
- `StorageRequestRecorder_QueryWithPartitionKey_IsNotReported`: filter built with `TableClient.CreateQueryFilter($"PartitionKey eq {value}")`.
- `CapturingLoggerProvider_AppLogsMessage_IsCaptured`
- `TestAuthenticationHandler_NoHeaders_LeavesCallerAnonymous`

### 7.4 API integration and security tests (Development, Azurite, collection StorageTestGroup)

`ApiTests/HealthEndpointTests.cs`

- `HealthEndpoint_AnonymousCallerWithHealthyStorage_Returns200WithStatusOnlyBody`: the body equals `Healthy` exactly.
- `HealthEndpoint_AnyCall_SendsNoStoreCacheControl`
- `HealthEndpoint_StorageCall_IsServicePropertiesRequestNotEntityQuery`: exactly one recorded request, with `IsEntityQuery` false.
- `HealthEndpoint_TwentyRequestsWithinCacheWindow_MakesOneStorageCall` (Security)
- `HealthEndpoint_RandomQueryStrings_MakesOneStorageCall` (Security; 20 requests with `?x={Guid}`)
- `HealthEndpoint_RandomHostHeaders_MakesOneStorageCall` (Security; 20 requests on an isolated factory, each with `Host: h{Guid:N}.example`). Exactly 1 storage call.
- `HealthEndpoint_RandomHostHeadersAndQueryStrings_MakesOneStorageCall` (Security; 20 requests, each with a random `Host` header and `?x={Guid}`). Exactly 1 storage call.
- `HealthEndpoint_PostRequest_Returns401WithoutStorageCall` (Security). 401 is the verified status: the framework's 405 endpoint falls under the fallback policy.
- `HealthEndpoint_CheckThrowsWithSensitiveMessage_Returns503WithStatusOnlyBody` (Security). Use `Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddHealthChecks().AddCheck<ThrowingHealthCheck>("throwing")))`. The status is 503 and the body equals `Unhealthy`, without the marker.
- `AliveEndpoint_AnonymousCaller_Returns200WithoutStorageCall`
- `AliveEndpoint_StorageCheckFailing_StillReturns200`: same derived factory with the throwing check, no `live` tag.

`ApiTests/AuthorizationTests.cs` (Security). Authorization matrix for every foundation endpoint. The callers that exist in the foundation are Unauthenticated and `TestUsers.AuthenticatedWithoutRole`. `feature/entra-sign-in` must add the other-tenant, guest, locked, no-role and admin callers as extra rows of these same theories.

| Endpoint | Unauthenticated | AuthenticatedWithoutRole | Test |
|---|---|---|---|
| `GET /health` | 200 | 200 | `HealthEndpoints_EveryCaller_Returns200` (Theory: path x caller) |
| `GET /alive` | 200 | 200 | same theory |
| `GET /openapi/v1.json` (Development) | 200 | 200 | `OpenApiDocument_Development_Returns200Anonymously` (Theory over caller) |
| `GET /scalar/v1` (Development) | 200 | 200 | `ApiReference_Development_Returns200Anonymously` (Theory over caller) |
| any other route (`GET` and `POST /does-not-exist`, `GET /api/v1/secrets`) | 401 | 404 | `UnknownRoute_AnonymousCaller_Returns401`, `UnknownRoute_AuthenticatedCaller_Returns404` (Theories over method and path) |

Also in this file:

- `Endpoints_EveryRoute_IsAllowListedAnonymousOrHasExplicitPolicy`: resolve `EndpointDataSource` from `Factory.Services`. Every `RouteEndpoint` is either:
  - anonymous (`IAllowAnonymous`) and its `RoutePattern.RawText` is on the allow-list `/health`, `/alive`, `/openapi/{documentName}.json`, `/scalar/{documentName?}`, `/scalar/scalar.js`, `/scalar/scalar.aspnetcore.js`, `/scalar/favicon.svg` (the exact verified patterns), or
  - non-anonymous with at least one `IAuthorizeData` or `AuthorizationPolicy` metadata item.
- `FallbackPolicy_Configured_RequiresAuthenticatedUser`: `IAuthorizationPolicyProvider.GetFallbackPolicyAsync()` is not null and contains a `DenyAnonymousAuthorizationRequirement`.
- `CorsPreflight_UntrustedOrigin_ReturnsNoAllowOriginHeader`: `OPTIONS /health` with `Origin: https://evil.example` and `Access-Control-Request-Method: GET`, and no `Access-Control-Allow-Origin` in the response.

`ApiTests/ErrorHandlingTests.cs` (Security)

- `UnhandledException_Development_ReturnsProblemDetailsWithoutDetails`: use `Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.Configure<OpenApiOptions>("v1", o => o.AddDocumentTransformer((document, context, token) => throw new InvalidOperationException(SensitiveMarkers.ExceptionMessage)))))` and `GET /openapi/v1.json`. Expect:
  - status 500
  - content type `application/problem+json`
  - a body without the marker, `StackTrace`, `InvalidOperationException` or ` at SharpVault`
  - security headers present

  This technique was verified.
- `UnknownRoute_AnonymousCaller_ReturnsProblemDetailsBody`: 401 with content type `application/problem+json`.
- `UnknownRoute_AnonymousCaller_ErrorResponseHasSecurityHeaders`

`ApiTests/SecurityHeadersTests.cs` (Security)

- `ApiResponse_AnyRoute_HasSecurityHeaders` (Theory: `/health`, `/alive`, `/does-not-exist`). Exact values from `SecurityHeaderValues` for CSP (API), X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Strict-Transport-Security, Permissions-Policy, Cross-Origin-Opener-Policy and Cross-Origin-Resource-Policy.
- `ApiResponse_WithoutExplicitCacheControl_SendsNoStore` (`/does-not-exist`)
- `ApiReference_Development_IsExemptFromContentSecurityPolicyButKeepsFrameDeny`

`ApiTests/LoggingTests.cs` (Security)

- `UnknownRoute_BearerTokenAndCookieSent_NeitherIsLogged`: send `Authorization: Bearer {ForgedTokens.SignedWithUnknownKey()}` and `Cookie: session={random}`. Neither value is in `Logs`, captured at Debug level.
- `UnknownRoute_SecretInQueryString_IsNotLogged`: `GET /does-not-exist?token={secret}`. The secret is not in `Logs`.
- `UnknownRoute_SecretInQueryString_IsNotInTraceTags`: an `ActivityListener` on `Microsoft.AspNetCore` and `System.Net.Http` records the display name and every tag of each stopped activity during `GET /does-not-exist?token={secret}`. At least one activity is captured and an `http.request.method` tag is present (so the check cannot pass vacuously), no value contains the secret, and no `url.query` tag exists.
- `UnknownRoute_SecretInQueryStringWithPackageRedactionDisabled_IsNotInTraceTags`: the same check on a factory with `UseSetting("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "true")`, which proves our code removes the query and not the package default.

`ApiTests/TableStorageStartupTests.cs`

- `Startup_TableSetConfigured_CreatesEveryTableBeforeFirstRequest`: a derived factory with `ConfigureTestServices(s => s.AddSingleton(new TableStorageTableSet(["FoundationProbeOne", "FoundationProbeTwo"])))`. After `CreateClient()`, `StorageAdminClient` lists both tables.
- `Startup_TableSetConfigured_MakesOneStorageCallPerTable`: the recorder is cleared before the derived factory starts, then counts exactly 2 requests and 0 entity queries.

### 7.5 Production hosting tests: `ApiTests/ProductionHostingTests.cs` (collection ProductionTestGroup, Security)

- `Startup_ProductionWithAccountKeyConnectionString_FailsWithoutEchoingKey`: use `Factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:tables", <Azurite well-known connection string>))`. `Record.Exception(() => derived.CreateClient())` is an `InvalidOperationException`, and its message contains neither `AccountKey` nor the key.
- `Startup_ProductionWithSasServiceUri_Fails` (`https://example.table.core.windows.net/?sv=2024-01-01&sig=abc`)
- `Startup_ProductionWithHttpServiceUri_Fails`
- `OpenApiDocument_Production_Returns401`
- `ApiReference_Production_Returns401` (`/scalar/v1`)
- `Endpoints_ProductionEnvironment_OnlyHealthEndpointsAreAnonymous`: the anonymous route patterns are exactly `/health` and `/alive`.
- `UnknownRoute_ProductionAnonymous_Returns401WithBearerChallenge`: `WWW-Authenticate` starts with `Bearer`.
- `UnknownRoute_ProductionAlgNoneToken_Returns401`
- `UnknownRoute_ProductionTokenSignedWithUnknownKey_Returns401`
- `UnknownRoute_ProductionForgedToken_TokenNotLogged`
- `ApiResponse_Production_HasStrictContentSecurityPolicy` (`/alive`, CSP equals the API value)
- `AliveEndpoint_Production_Returns200Anonymously`

### 7.6 Web tests: `WebTests/WebHostTests.cs` (collection WebTestGroup)

- `HomePage_AnonymousGet_Returns200WithHeading` (`id="home-heading"`)
- `HomePage_Response_HasWebSecurityHeaders` (Security; exact web CSP and the other headers)
- `HomePage_Rendered_ContainsNoInlineScript` (Security). The regex `<script(?![^>]*\bsrc=)[^>]*>` has zero matches in the HTML. Use `[GeneratedRegex]` in a partial test class.
- `HomePage_Response_SendsNoStore` (Security)
- `UnknownPage_Get_Returns404WithNotFoundPage` (`id="not-found-heading"`)
- `UnhandledException_Web_RendersErrorPageWithoutExceptionDetails` (Security). A derived factory registers `ThrowingRequestStartupFilter`, then `GET /test/throw`. Expect:
  - status 500
  - the body contains `id="error-heading"`
  - the body contains neither the marker nor `StackTrace`
- `WebHealth_AnonymousGet_Returns200`
- `WebHost_SecretInQueryString_IsNotLogged` (Security): `GET /does-not-exist?token={secret}` on the Web. The Web factory captures logs, at least one entry is captured, and none contains the secret.

### 7.7 AppHost model tests: `tests/SharpVault.AppHost.Tests/AppHostModelTests.cs`

No collection needed (no shared state). Build with `DistributedApplicationTestingBuilder.CreateAsync<Projects.SharpVault_AppHost>` and inspect `builder.Resources`.

- `AppHost_StorageEmulator_UsesPersistentLifetime`: `ContainerLifetimeAnnotation.Lifetime == ContainerLifetime.Persistent`
- `AppHost_StorageEmulator_MountsDataVolumeAtData`: a `ContainerMountAnnotation` with `Type == ContainerMountType.Volume` and `Target == "/data"`
- `AppHost_StorageEmulator_UsesAzuriteImage`: `ContainerImageAnnotation` registry `mcr.microsoft.com`, image `azure-storage/azurite`
- `AppHost_TablesResource_NameMatchesInfrastructureConnectionName`: a resource named `TableStorageConnection.Name` exists
- `AppHost_ApiProject_WaitsForTables` (`WaitAnnotation` on the `api` resource targeting `tables`)
- `AppHost_WebProject_WaitsForApi`
- `AppHost_Model_DeclaresNoParameterResources` (Security): no resource in `builder.Resources` is a `ParameterResource`, so the AppHost declares no secret or key parameters.
- `AppHost_Source_ContainsNoParametersEnvironmentOverridesOrKeys` (Security; Theory: `AddParameter`, `WithEnvironment`, `AccountKey`, `SharedAccessSignature`, `ConnectionString`, `secret:`): `src/SharpVault.AppHost/AppHost.cs` does not contain the value (OrdinalIgnoreCase).
- Evaluating the project resources' environment variables was dropped: `ExecutionConfigurationBuilder` hangs when the resources are not running, and these tests never start them. The parameter-resource check and the source scan replace it.

### 7.8 Performance tests: `ApiTests/HealthPerformanceTests.cs` (collection StorageTestGroup, Performance)

Warm up with one request, then measure with `Stopwatch.GetTimestamp`/`GetElapsedTime`.

- `HealthEndpoint_FirstUncachedRequest_CompletesUnder1000Milliseconds`
- `HealthEndpoint_FiftyCachedRequests_EachCompletesUnder100Milliseconds`
- `AliveEndpoint_FiftyRequests_EachCompletesUnder50Milliseconds`
- `HealthEndpoint_ConcurrentRequestsOnColdCache_MakesAtMostThreeStorageCalls`: 20 parallel `GET /health` on a fresh isolated host all return 200 and make at most 3 storage calls (`MaxColdBurstStorageCalls`), none of them a query without a partition key. Output-cache coalescing bounds the misses in a cold concurrent burst but does not guarantee exactly one, so this test allows up to 3. The bound is fixed and does not grow with the number of requests. Tests with a single miss (`HealthEndpoint_StorageCall_IsServicePropertiesRequestNotEntityQuery` and every `*_MakesOneStorageCall` test) still assert exactly 1.

### 7.9 Commands the reviewer runs

```bash
docker info
dotnet build SharpVault.slnx
dotnet test SharpVault.slnx --filter "Category!=Performance"
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
dotnet test SharpVault.slnx -c Release --filter "Category!=Performance" --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults
dotnet tool restore
dotnet reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" "-targetdir:TestResults/coverage" "-reporttypes:Cobertura"
dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80
dotnet list SharpVault.slnx package --vulnerable --include-transitive
```

## 8. Performance budgets

All budgets are measured against Azurite in integration tests.

| Operation | Budget | Storage calls | Test |
|---|---|---|---|
| `GET /health`, cache miss | under 1000 ms | exactly 1 (service properties, not an entity query) | `HealthEndpoint_FirstUncachedRequest_CompletesUnder1000Milliseconds`, `HealthEndpoint_StorageCall_IsServicePropertiesRequestNotEntityQuery` |
| `GET /health`, within the 5 s cache window, any query string or `Host` header | under 100 ms each | 0 | `HealthEndpoint_FiftyCachedRequests_EachCompletesUnder100Milliseconds`, `HealthEndpoint_TwentyRequestsWithinCacheWindow_MakesOneStorageCall`, `HealthEndpoint_RandomQueryStrings_MakesOneStorageCall`, `HealthEndpoint_RandomHostHeaders_MakesOneStorageCall`, `HealthEndpoint_RandomHostHeadersAndQueryStrings_MakesOneStorageCall` |
| `GET /health`, cold cache, 20 concurrent requests | n/a | at most 3 in total, a fixed bound that does not grow with the request count (output-cache coalescing) | `HealthEndpoint_ConcurrentRequestsOnColdCache_MakesAtMostThreeStorageCalls` |
| `POST /health` | n/a | 0 | `HealthEndpoint_PostRequest_Returns401WithoutStorageCall` |
| `GET /alive` | under 50 ms each | 0 | `AliveEndpoint_FiftyRequests_EachCompletesUnder50Milliseconds`, `AliveEndpoint_AnonymousCaller_Returns200WithoutStorageCall` |
| Any unknown route | n/a | 0 | asserted inside `UnknownRoute_AnonymousCaller_Returns401` (recorder count 0) |
| API startup | n/a | 1 per entry in `TableNames.All` (0 today), never per request | `Startup_TableSetConfigured_MakesOneStorageCallPerTable` |

- **Queries without a partition key**: none are allowed. The foundation makes no entity queries. The detector is proven by `StorageRequestRecorder_QueryWithoutPartitionKey_IsReported`.
- **Hot paths and benchmarks**: none in this work. `tests/SharpVault.Benchmarks` is created by `feature/encryption-core`, together with the first hot path.

## 9. Risks and decisions

| # | Topic | Decision and reason |
|---|---|---|
| D1 | MAUI | **`SharpVault.Maui` is not created now.** The MAUI workload cannot be installed here (builds.dotnet.microsoft.com is blocked), so the project could not even be evaluated. Adding it to `SharpVault.slnx` would break `dotnet build` on Linux. `feature/maui-app` adds the project to `SharpVault.slnx`, adds `SharpVault.Linux.slnf` (every project except Maui), switches the Linux CI job to the filter, and adds a separate macOS or Windows job with the workload. Until then the Linux build path is the whole solution. |
| D2 | Test runner | xunit.v3 4.x defaults to Microsoft Testing Platform v2. On SDK 10, `dotnet test` in VSTest mode then fails with "Testing with VSTest target is no longer supported" (verified). We use **`xunit.v3.mtp-off` with VSTest**. This keeps the skill's `--filter "Category!=Performance"` syntax and `coverlet.collector` working (verified). The MTP alternative also works (`global.json` test runner, `coverlet.MTP`, `--filter-not-trait`) but would change every documented command. Revisit when the skills are updated. |
| D3 | Coverage gate | Neither coverlet.collector nor ReportGenerator 5.5.12 can fail a build on a threshold. ReportGenerator has no `minimumCoverageThresholds` option (verified). We merge with ReportGenerator and gate with `eng/CoverageGate.cs`, a .NET 10 file-based app. It gates by namespace prefix, so `feature/encryption-core` can add `SharpVault.Infrastructure.Crypto=95` without changing the tool. It is build tooling: it has no unit tests, and T9 checks it by hand. |
| D4 | Default authorization | **Decided now:** a fallback policy `RequireAuthenticatedUser` plus a placeholder JwtBearer default scheme. Verified: with a fallback policy but no scheme, every unknown route throws "No authenticationScheme was specified" and returns 500. JwtBearer with no keys returns 401 for missing and forged tokens. `feature/entra-sign-in` replaces `.AddJwtBearer()` with Microsoft.Identity.Web and must decide whether the fallback should require a SharpVault role (TEN-4). If it does, the `AuthenticatedWithoutRole` row for unknown routes changes from 404 to 403. JwtBearer can read `Authentication:Schemes:Bearer:SigningKeys` from configuration. That needs configuration access, and entra-sign-in owns the final scheme setup. |
| D5 | Web authorization | The Web has no authentication scheme yet, so it has no fallback policy (it would return 500 everywhere, see D4). It serves only a static shell with no data. `feature/entra-sign-in` adds OIDC and the fallback policy. |
| D6 | `AddInfrastructure` signature | `AddInfrastructure(this IHostApplicationBuilder builder)` instead of the skill's `(IServiceCollection, IConfiguration)`, because the Aspire client registration (`AddAzureTableServiceClient`) needs the host builder. |
| D7 | Storage authentication | `ConnectionStrings:tables` holds the https service URI in Azure, and Aspire uses `DefaultAzureCredential` (set explicitly with `??=`). Connection strings, account keys and SAS are accepted only in Development, enforced by the guard inside Aspire's `configureSettings`, which runs during registration (verified). Stores never check the environment. |
| D8 | Health endpoints in every environment | This differs from the Aspire template, which maps them only in Development, because Azure probes need them. They are status-only, anonymous, GET and HEAD only, `/health` is output-cached for 5 s with no query vary, and `/alive` checks only the process. |
| D9 | HTTPS | No `UseHttpsRedirection` and no `UseHsts`. Platform probes use http, and API clients must not follow redirects. Our middleware sends HSTS on every response; browsers ignore it over http (RFC 6797), and it may also pin `localhost` to https in developers' browsers, which is accepted. The web CSP adds `upgrade-insecure-requests`. **The deployment feature must configure HTTPS-only ingress.** |
| D10 | Error details | `UseExceptionHandler()` in every environment, so Development behaves like production and tests (which must run in Development, because the development key provider refuses other environments) prove no leakage. Developers read exceptions in logs and in the Aspire dashboard. |
| D11 | Security headers location | In ServiceDefaults, because the Api and Web share it. Scalar is exempt from CSP only in Development, because its UI needs scripts. |
| D12 | Architecture tests | Reflection with `GetReferencedAssemblies` instead of NetArchTest. The BCL is enough (coding standards: do not add a package the base library covers), and NetArchTest.Rules 1.3.2 dates from 2021 and pins Mono.Cecil 0.11.3. |
| D13 | Scope | No `Result<T>`, pipeline behaviors, `ICurrentUser`, store interfaces or `docs/STORAGE.md` tables. The first feature that needs each adds it, so nothing speculative is written now. |
| D14 | Lock files | No `packages.lock.json`. The AppHost restores operating-system-specific DCP and dashboard packages, so `--locked-mode` would fail on a different OS. Supply chain is covered by CPM exact versions, transitive pinning, NuGet audit (all, low, as errors), the CI vulnerable step, dependency review, CodeQL and Dependabot. |
| D15 | ASPIRE010 | Suppressed in the AppHost only. `AspireUseCliBundle=true` needs the Aspire CLI, which is not available here. DCP and the dashboard from NuGet run the AppHost on Linux (verified: dashboard up, Azurite container and named volume created). |
| D16 | CA1707 and CA1711 | CA1707 is disabled only under `tests/`, because the required test names contain underscores. CA1711 forbids the `Collection` suffix that the skill uses for xUnit collection definitions (`DatabaseTestCollection`). It is fixed by renaming, not suppressing: the classes are `StorageTestGroup`, `ProductionTestGroup` and `WebTestGroup`. Any other analyzer that fires because of test conventions must be reported to the planner, not suppressed. |
| D17 | Two `Program` types | Api and Web both expose a compiler-generated global `Program`. Test projects reference Web with `Aliases="WebApp"` and write `WebApp::Program` (verified). `SharpVault.AppHost.Tests` is a separate project so Aspire hosting stays out of the API tests. |
| D18 | Assertions | xUnit `Assert` only. No FluentAssertions (commercial license since v8) and no other assertion library. |
| D19 | GitHub Actions pinning | The session could not reach the GitHub API, so commit SHAs could not be resolved. Workflows use major tags (`checkout@v5`, `setup-dotnet@v5`, `upload-artifact@v4`, `codeql-action@v4`, `dependency-review-action@v4`). Dependabot keeps them current, and pinning to SHAs is recorded in PROGRESS.md as a follow-up. If a tag does not exist when CI first runs, use the latest major of that action. |
| D20 | Theme and fonts | `app.css` defines variables only, and no external fonts load (CSP `font-src 'self'`). The distinctive theme required by the UI skill is designed in the first real UI feature, with bundled font files. |
| D21 | Web CSP | Strict: `script-src 'self'` and no `<ImportMap />`. `dotnet watch` browser refresh may be blocked by `connect-src 'self'`, which is accepted. `form-action 'self'` must be reviewed by entra-sign-in for the OIDC sign-out form. |
| D22 | Request size | 1 MiB global Kestrel limit. Endpoints that need more (later attachments) opt in per endpoint. TestServer does not enforce Kestrel limits, so the test checks the options. |
| D23 | Playwright | Not set up now: there is no interactive UI, and browser downloads may be blocked here. The first feature with interactive pages adds `WebTestFactory` E2E support and page objects. |
| D24 | Mediator persistence | Explicitly off. The default persistence writes notifications to local files. |
| D25 | OpenAPI in tests | Test projects must not call `AddOpenApi`, because the OpenAPI source generator then fails with CS9137 (interceptors). Use `Configure<OpenApiOptions>("v1", ...)` (verified). |
| D26 | xUnit analyzers | `xUnit1051` requires `TestContext.Current.CancellationToken`. `xUnit2013` requires `Assert.Single` or `Assert.Empty` for size checks. Both are errors under warnings as errors. |
| D27 | Production factory | Never calls `/health`, which would try `DefaultAzureCredential` against a fake account. The table initializer is removed in that factory. Startup in Production with an https URI was verified to make no network call. |
| D28 | Razor `_Imports` and the sealed rule | The Razor compiler emits an unsealed `_Imports` class for each `_Imports.razor`. It has neither `CompilerGeneratedAttribute` nor `GeneratedCodeAttribute`, so it cannot be excluded by attribute, and the generated class cannot be sealed. `ProductionAssemblies_ConcreteClasses_AreSealed` therefore excludes it by name plus exact shape: the name is `_Imports` and the declared members are only constructors and a method named `Execute`. A hand-written type named `_Imports` with any other member is still checked. This is the only name-based exclusion. |

## 10. Definition of done for this branch

- All tasks T1 to T14 are complete. `dotnet build SharpVault.slnx` reports 0 warnings and 0 errors.
- Every test in section 7 exists and passes, including the Security and Performance categories.
- The coverage gate passes with Application and Infrastructure at 80% or more line coverage.
- `dotnet list SharpVault.slnx package --vulnerable --include-transitive` reports no vulnerable packages.
- No inline comments, commented-out code or TODOs in `.cs`, `.razor`, `.js`, `.css`, `.csproj` or `.props` files. The AGPL header is on every `.cs` file, including `eng/CoverageGate.cs`.
- No connection strings, keys or secrets in any `appsettings*.json` or `launchSettings.json`.
- The reviewer has approved. The pull request title is `feat: solution foundation`, and it says "foundation (no requirement id)".
