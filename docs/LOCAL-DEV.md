# Local development

## Prerequisites

- .NET 10 SDK. `global.json` pins 10.0.100 with `rollForward` set to `latestFeature`, and pins the Aspire SDK to 13.6.4.
- Docker, running. The AppHost runs Azurite in a container, and the integration tests start their own Azurite container.
- Optional: the HTTPS profile needs a trusted development certificate. If it fails with a certificate error, run `dotnet dev-certs https --trust`.

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

The launch profiles in `src/SharpVault.AppHost/Properties/launchSettings.json` set `ASPNETCORE_ENVIRONMENT` to `Development`, so the API serves OpenAPI at `/openapi/v1.json` and Scalar at `/scalar`.

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

The solution is `SharpVault.slnx`. Maui is not part of the Linux build (it is planned in `feature/maui-app`), so the whole solution builds and tests on Linux today.

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
dotnet run eng/CoverageGate.cs -- TestResults/coverage/Cobertura.xml SharpVault.Application=80 SharpVault.Infrastructure=80
```

`eng/CoverageGate.cs` fails the command when a namespace prefix is below its threshold. ReportGenerator cannot enforce thresholds itself.

## Vulnerable packages

```bash
dotnet list SharpVault.slnx package --vulnerable --include-transitive
```

CI fails when this reports vulnerable packages. Restore also runs NuGet audit for all packages, including transitive ones, at low severity and above, and warnings are errors.

## Build warnings

`Directory.Build.props` sets warnings as errors, so a warning fails the build. `dotnet build SharpVault.slnx` should report `0 Warning(s)` and `0 Error(s)`.
