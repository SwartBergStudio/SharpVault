# Local development

## Prerequisites

- .NET 10 SDK. `global.json` pins 10.0.100 with `rollForward` set to `latestFeature`, and pins the Aspire SDK to 13.6.4.
- Docker, running. The AppHost runs Azurite in a container, and the integration tests start their own Azurite container.
- Optional: the HTTPS profile needs a trusted development certificate. If it fails with a certificate error, run `dotnet dev-certs https --trust`.
- Entra ID settings for `SharpVault.Api` and `SharpVault.Web`, set with `dotnet user-secrets`. Both projects stop at startup without `AzureAd:TenantId` and `AzureAd:ClientId`. The steps are in [ENTRA-SETUP.md](ENTRA-SETUP.md#6-local-settings-with-user-secrets).

Check Docker before you start:

```bash
docker info
```

## Run the AppHost

```bash
dotnet run --project src/SharpVault.AppHost
```

This starts the Aspire AppHost with these resources:

- `storage`: Azurite, run as an emulator container with a persistent lifetime and a named data volume.
- `tables`: the Table Storage endpoint on that emulator. The API references it.
- `api`: `SharpVault.Api`, which waits for `tables` and is checked on `/health`.
- `web`: `SharpVault.Web`, which waits for `api` and is exposed as an external endpoint.

The AppHost does not set any Entra ID value. `api` and `web` read `AzureAd:TenantId` and `AzureAd:ClientId` from their own user secrets, so set them before the first run. If they are missing, `api` and `web` fail at startup with a configuration error. The keys, the Entra app registrations and the commands are in [ENTRA-SETUP.md](ENTRA-SETUP.md).

The launch profiles in `src/SharpVault.AppHost/Properties/launchSettings.json` set `ASPNETCORE_ENVIRONMENT` to `Development`, so the API serves OpenAPI at `/openapi/v1.json` and Scalar at `/scalar`.

## Development encryption key

In run mode the AppHost adds a secret parameter, `development-key-material`, and passes it to `api` as `Encryption__DevelopmentKeyMaterial`. The API then wraps data keys with the development key provider, which only runs in the Development environment. The AppHost generates the value once, with 64 characters, and stores it in the AppHost user secrets under `Parameters:development-key-material`. Later runs reuse it, so data in the persistent Azurite volume stays readable.

Publish mode declares no parameter, and the web app receives no encryption settings. To reset the key, remove it together with the local data, because data encrypted with the old key cannot be decrypted with a new one:

```bash
dotnet user-secrets remove Parameters:development-key-material --project src/SharpVault.AppHost
```

Then wipe the Azurite volume as described in [Wipe local data](#wipe-local-data). The design and the Key Vault setup are in [ENCRYPTION.md](ENCRYPTION.md).

Profiles:

- `https` (the default): dashboard at `https://localhost:17260`.
- `http`: dashboard at `http://localhost:15260`.

Choose a profile with:

```bash
dotnet run --project src/SharpVault.AppHost --launch-profile http
```

The dashboard shows the URLs of `api` and `web`.

## Persistent Azurite

The storage emulator uses `ContainerLifetime.Persistent`, so the container keeps running after the AppHost stops, and it keeps its data in a named Docker volume. Your local data survives between debug sessions.

Stop the container without losing data:

```bash
docker ps --filter "ancestor=mcr.microsoft.com/azure-storage/azurite:3.35.0"
docker stop <container-id>
```

## Wipe local data

Remove the container and the volume. Aspire names the volume with the `-storage-data` suffix, so find it first:

```bash
docker ps -a --filter "ancestor=mcr.microsoft.com/azure-storage/azurite:3.35.0"
docker rm -f <container-id>
docker volume ls --filter "name=storage-data"
docker volume rm <volume-name>
```

The next AppHost run creates a fresh container and volume.

## Run the tests

The solution is `SharpVault.slnx`. Maui is not part of the Linux build (it is planned in `feature/maui-app`), so the whole solution builds and tests on Linux today. No test needs an Entra tenant: the tests sign tokens with locally generated keys.

```bash
dotnet build SharpVault.slnx
dotnet test SharpVault.slnx --filter "Category!=Performance"
```

Filters:

```bash
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
dotnet test SharpVault.slnx --filter "FullyQualifiedName~HealthEndpointTests"
```

- `Category=Security` selects the security tests.
- `Category=Performance` selects the timing tests in `PerformanceTestGroup`. They run only in CI on pushes to `main`.
- `Category!=Performance` is the default for local runs and for CI pull requests.

The test projects, collections and helpers are described in [TESTING.md](TESTING.md).

## Coverage

```bash
dotnet test SharpVault.slnx -c Release --filter "Category!=Performance" --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults
dotnet tool restore
dotnet reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" "-targetdir:TestResults/coverage" "-reporttypes:Cobertura"
dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80 SharpVault.Domain.Encryption=95 SharpVault.Infrastructure.Encryption=95
```

The thresholds are 80% for `SharpVault.Application` and `SharpVault.Infrastructure`, and 95% for the encryption namespaces `SharpVault.Domain.Encryption` and `SharpVault.Infrastructure.Encryption`. `eng/CoverageGate.cs` fails the command when a namespace prefix is below its threshold. ReportGenerator cannot enforce thresholds itself. The benchmark project is excluded from coverage in `tests/coverage.runsettings`.

## Benchmarks

`tests/SharpVault.Benchmarks` contains BenchmarkDotNet benchmarks for the encryption hot paths: `Encrypt`, `Decrypt` and `ReEncrypt` on 32 bytes, 1 KiB and 32 KiB, and `CreateDataKey`, `OpenDataKey` and `RewrapDataKey` on the development provider. The Key Vault paths are network bound and are covered by call-count tests instead.

Run them in Release, with the short job for real numbers:

```bash
dotnet run -c Release --project tests/SharpVault.Benchmarks -- --filter '*' --job short
```

The benchmark project is not a test project. `dotnet test` does not run it. Pull requests that touch these paths paste the before and after tables into the description. CI runs `--job dry` as a smoke test on pushes to `main`. The run fails only when a benchmark fails, and the results are not compared.

## Vulnerable packages

```bash
dotnet list SharpVault.slnx package --vulnerable --include-transitive
```

CI fails when this reports vulnerable packages. Restore also runs NuGet audit for all packages, including transitive ones, at low severity and above, and warnings are errors.

## Build warnings

`Directory.Build.props` sets warnings as errors, so a warning fails the build. `dotnet build SharpVault.slnx` should report `0 Warning(s)` and `0 Error(s)`.
