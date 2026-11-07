<div align="center">

# 🔐 SharpVault

**An open-source password and secrets manager for organisations, built with C# and .NET.**

Blazor web app · .NET MAUI mobile and desktop apps · Azure Table Storage · Microsoft Entra ID

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Web-512BD4)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![.NET MAUI](https://img.shields.io/badge/.NET%20MAUI-Mobile-512BD4)](https://dotnet.microsoft.com/apps/maui)
[![Aspire](https://img.shields.io/badge/Aspire-Local%20dev-512BD4)](https://learn.microsoft.com/dotnet/aspire/)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)
[![CI](https://github.com/SwartBergStudio/SharpVault/actions/workflows/ci.yml/badge.svg)](https://github.com/SwartBergStudio/SharpVault/actions/workflows/ci.yml)

[![Buy Me A Coffee](https://img.shields.io/badge/Buy%20Me%20A%20Coffee-ffdd00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/swartbergstudio)

</div>

---

> ⚠️ **Status: Early development.** SharpVault has not been independently security-audited. Please don't use it as your only store for important credentials yet.
>
> So far the repository contains the solution foundation (build, local Aspire setup, storage registration, CI), the encryption core (envelope encryption and key wrap providers), Microsoft Entra ID sign-in limited to one tenant with app roles, a local development sign-in for Development runs, and the Vault Steel theme on MudBlazor. The vault (secrets, folders, history, recycle bin and the password generator) is built on the `feature/local-debug-auth` branch, together with sharing with colleagues (Stage 5) and admin lock, unlock and transfers with second-admin approval (Stage 6). The work since the last commit is not committed or merged yet. One-time share links, scheduled locks and audit log search are not built. The unticked features below are planned and not built. Status and open items are in [docs/PROGRESS.md](docs/PROGRESS.md).

## ✨ Why SharpVault?

SharpVault is a team password manager for organisations that already run on Microsoft Entra ID and Azure. It is built end to end in **C# and .NET**, so .NET developers can read, audit, extend and self-host every layer.

- **Sign in with your organisation account**: Microsoft Entra ID with app roles for admins and users.
- **Cheap to run**: secrets live in Azure Table Storage, accessed with Entra role-based access (no storage keys).
- **Your keys, your Key Vault**: every secret is encrypted with its own key, wrapped by a key in your Azure Key Vault.
- **Built for staff changes**: when someone leaves, is dismissed or passes away, admins lock them out and transfer their secrets, with a second admin's approval and a full audit trail.
- **Private by default**: each user sees only their own vault and what is shared with them. Admin rights do not unlock anyone else's secrets.
- **One codebase, every platform**: a shared Razor Class Library powers both the Blazor web app and the .NET MAUI apps.
- **Open source**: transparent code you can inspect and trust.

## 🧩 Features

The full specification is in [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md).

### Vault
- [x] Encrypted vault for logins, secure notes, API keys and cards (see [docs/VAULT.md](docs/VAULT.md))
- [x] Folders and sub-folders, tags and search
- [x] Strong password and passphrase generator
- [x] Secret history with restore, and a recycle bin (30 days, then purged)

### Sharing (see [docs/SHARING.md](docs/SHARING.md))
- [x] Share secrets or whole folders with colleagues in your Entra tenant (view or edit; only the owner can give manage). Local development uses the four local accounts.
- [x] Shared with me, grouped by owner, with filing into your own folders
- [x] Revoke a share at any time
- [x] One-time share links for anyone, destroyed after one view or when they expire
- [x] Each user sees only their own vault and what is shared with them

### Administration (see [docs/TRANSFERS.md](docs/TRANSFERS.md))
- [x] Lock users out: their next API call is refused, and an open page ends within about a minute (scheduled locks are not built yet)
- [x] See a user's secret count and the shares they have given, on demand, with no names (the list itself makes no count calls)
- [x] Transfer a user's vault when they leave, are dismissed or change role
- [x] Every transfer needs a written reason and approval by a second admin
- [x] Admins never see other users' secrets unless they receive them through an approved transfer or a share
- [ ] Audit log search and CSV export (audit entries are already written for every reveal, share, lock and transfer)

### Security
- [x] AES-256-GCM encryption with a unique key per secret, wrapped by your Azure Key Vault (the Key Vault provider is tested with fakes and is not provisioned yet)
- [x] Microsoft Entra ID sign-in, limited to your tenant, with `SharpVault.Admin` and `SharpVault.User` roles
- [ ] Managed identity access to Table Storage and Key Vault, no secrets in configuration
- [x] Clipboard auto-clear and idle auto-lock (the idle timeout is set by configuration, the clipboard timeout is fixed, and there is no admin screen yet)

### Platforms
- [ ] Blazor web app
- [ ] .NET MAUI for Android, iOS, Windows and macOS

### Later
- [ ] Import from Keeper, Bitwarden, 1Password and CSV
- [ ] TOTP codes, attachments and a password health report
- [ ] Browser extension and mobile autofill
- [ ] Sharing with Entra groups

## 🔒 Security model

SharpVault uses **server-side envelope encryption**. The encryption core is built and the vault stores secrets with it. The design and formats are in [docs/ENCRYPTION.md](docs/ENCRYPTION.md).

```
Secret value ──► AES-256-GCM (random data key + nonce) ──► Encrypted value ──► Azure Table Storage
                         │
      Data key ──► wrapped by Key Vault key (RSA-OAEP-256) ──► Wrapped key stored with the secret
```

What exists today:

- AES-256-GCM envelopes (version 1) with a random data key per item. The purpose, item id and owner id are bound to each ciphertext as associated data.
- A Key Vault key wrap provider (RSA-OAEP-256, through the app's managed identity). It is tested with fakes. Provisioning the vault, the key and the role assignment is part of deployment and is not done yet.
- A development key wrap provider for local runs. It refuses to run outside the Development environment, and the AppHost generates its key material.
- Entra ID sign-in is built for one tenant. The API accepts only v2 access tokens for its own scope, from the configured tenant, with a `SharpVault.User` or `SharpVault.Admin` role. Every request also checks the user's lock status. Setup is in [docs/ENTRA-SETUP.md](docs/ENTRA-SETUP.md).

Secret values and notes are decrypted only when the owner reveals or copies them, and every reveal and copy is audited before the text is returned. Names and other metadata are decrypted to build lists and search results. The vault rules are in [docs/VAULT.md](docs/VAULT.md) and the storage layout is in [docs/STORAGE.md](docs/STORAGE.md).

**This is not a zero-knowledge design, on purpose.** Because admins must be able to transfer secrets when people leave, the application (through your Key Vault) can decrypt secrets. Whoever controls your Azure tenant and Key Vault controls access to the data, so protect those accordingly.

## 🏗️ Architecture

SharpVault follows **Clean Architecture**, with a shared UI layer so web and mobile stay in sync.

```
SharpVault/
├── SharpVault.slnx                   # Solution (Linux build and tests)
├── global.json, Directory.Build.props, Directory.Packages.props
├── src/
│   ├── SharpVault.Dtos/              # API contracts shared by the API, the Web and the UI (no logic)
│   ├── SharpVault.Domain/            # Core types (users, audit entries, vault items, maintenance lease)
│   ├── SharpVault.Application/       # CQRS via SwartBerg.Mediator, mapping, validation, user access gate, vault features
│   ├── SharpVault.PasswordGeneration/ # Password and passphrase generator (RandomNumberGenerator, EFF word list)
│   ├── SharpVault.Infrastructure/    # Azure Table Storage (users, audit log, vaults, maintenance), encryption core and key wrap providers
│   ├── SharpVault.Authentication/    # Entra ID identity, authentication mode (Entra or local) and the local development sign-in
│   ├── SharpVault.Api/               # ASP.NET Core minimal API host
│   ├── SharpVault.UI.Shared/         # Razor Class Library (shared UI on MudBlazor, API clients)
│   ├── SharpVault.Web/               # Blazor web app host
│   ├── SharpVault.Maui/              # .NET MAUI Blazor Hybrid app (planned in feature/maui-app)
│   ├── SharpVault.AppHost/           # Aspire orchestration for local development
│   └── SharpVault.ServiceDefaults/   # OpenTelemetry, health checks, security headers, request limits
├── tests/
│   ├── SharpVault.UnitTests/
│   ├── SharpVault.IntegrationTests/  # Api and Web hosts with Azurite (Testcontainers)
│   ├── SharpVault.E2ETests/          # Browser journeys in system Chrome against the built API and Web (Category=E2E, plus the SiteTour)
│   ├── SharpVault.AppHost.Tests/     # Aspire AppHost model tests
│   └── SharpVault.Benchmarks/        # BenchmarkDotNet benchmarks for encryption (not a test project)
├── eng/
│   └── CoverageGate.cs               # Coverage threshold check used in CI
├── docs/                             # Requirements, progress, local development, storage, encryption, vault, sharing, transfers, testing, Entra setup, theme
└── .github/                          # CI and Dependabot
```

## 🛠️ Tech Stack

| Layer        | Technology                                          |
|--------------|-----------------------------------------------------|
| Language     | C# / .NET 10                                        |
| Web UI       | Blazor                                              |
| Mobile       | .NET MAUI Blazor Hybrid (planned)                   |
| API          | ASP.NET Core Minimal APIs, OpenAPI, Scalar          |
| Application  | SwartBerg.Mediator (CQRS), Mapster, FluentValidation |
| Storage      | Azure Table Storage (`Azure.Data.Tables`)           |
| Keys         | Azure Key Vault (provider built, not provisioned)   |
| Auth         | Microsoft Entra ID, Microsoft.Identity.Web (one tenant, app roles); local sign-in for Development runs |
| Theme        | Vault Steel on MudBlazor 9.11.0, self-hosted Archivo and JetBrains Mono, see [docs/THEME.md](docs/THEME.md) |
| Local dev    | .NET Aspire with a persistent Azurite container     |
| Testing      | xUnit, bUnit, Testcontainers (Azurite), AutoFixture |

## 🚀 Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Aspire runs Azurite in a container, and the integration tests start one)
- No Entra ID tenant is needed to run locally. With no Entra ID settings in Development, the web app offers a sign-in picker with four local development accounts. See [docs/LOCAL-DEV.md](docs/LOCAL-DEV.md).
- To sign in with Entra ID, you need a tenant with two app registrations. The repository ships placeholders only. See [docs/ENTRA-SETUP.md](docs/ENTRA-SETUP.md). The tests do not need a tenant.
- The .NET MAUI workload is not needed yet, because the MAUI project is planned in `feature/maui-app`.

### Clone and run

```bash
git clone https://github.com/SwartBergStudio/SharpVault.git
cd SharpVault
dotnet run --project src/SharpVault.AppHost
```

With no Entra ID settings in Development, the web app starts in local mode. Its sign-in page is a picker with four local accounts, and no Entra ID tenant is needed. To sign in with Entra ID instead, set the settings described in [docs/ENTRA-SETUP.md](docs/ENTRA-SETUP.md). Both hosts read the same settings at startup. They stop with an error if only one of the two ids is set, or if neither is set outside Development.

After startup the Aspire dashboard shows the API, the web app and Azurite. Azurite uses a persistent container and data volume, so your local data survives between debug sessions. For the HTTPS profile, run `dotnet dev-certs https --trust` if you get a certificate error.

The web app has a home page with a sign-in link. Signed-in users who open `/` go to the vault, and the side navigation has no Home link. It also has a vault for signed-in users (secrets, folders, history and the recycle bin), a "Shared with me" page, an account page, an admin users page for administrators, an admin transfers page for administrators (new transfers, approvals and the transfer detail), and access-denied, signed-out, sign-in-error and not-found pages. The API answers `/health` and `/alive` anonymously, returns 401 without a valid token, and returns 403 for a signed-in caller who has no accepted role or who is locked. The OpenAPI document and Scalar are available in Development only. Local mode details are in [docs/LOCAL-DEV.md](docs/LOCAL-DEV.md).

See [docs/LOCAL-DEV.md](docs/LOCAL-DEV.md) for profiles, wiping local data, local mode and coverage commands, and [docs/THEME.md](docs/THEME.md) for the theme.

### Run the tests

```bash
dotnet test SharpVault.slnx --filter "Category!=Performance&Category!=E2E&Category!=SiteTour"
```

Integration tests start their own Azurite container with Testcontainers, so Docker must be running. The browser journeys (`Category=E2E`) need Docker, the system Chrome and a built API and Web. The screenshot tour (`Category=SiteTour`) is run by hand. See [docs/TESTING.md](docs/TESTING.md) for the test projects, helpers and the commands.

## 🗺️ Roadmap

- [ ] **v0.1**: Solution foundation, encryption, Table Storage, Entra sign-in limited to one tenant
- [ ] **v0.2**: Vault with folders, password generator, secret history
- [ ] **v0.3**: Sharing with colleagues and one-time share links
- [ ] **v0.4**: User lock, offboarding transfers with second-admin approval, audit log
- [ ] **v0.5**: .NET MAUI apps
- [ ] **v1.0**: Independent security audit and stable release

## 🤝 Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) first. In short:

1. Create a branch from `main` named `feature/{description}` or `fix/{description}`
2. Commit using conventional commits (`feat:`, `fix:`, `docs:` ...)
3. Open a pull request to `main` for review by the code owner

## 🛡️ Security

**Please do not report security vulnerabilities through public GitHub issues.**

Report vulnerabilities privately through [GitHub Security Advisories](https://github.com/SwartBergStudio/SharpVault/security/advisories/new). See [SECURITY.md](SECURITY.md) for details.

## 📄 License

SharpVault is licensed under the [GNU Affero General Public License v3.0](LICENSE).

In short, you're free to use, study, modify and share SharpVault. If you distribute a modified version, or run one as a network service for others, you must also make your source code available under the AGPL-3.0.

Copyright © 2026 SwartBergStudio

---

<div align="center">

Built with 💜 and C#

⭐ If you find SharpVault useful, please give it a star!

☕ Or support development:

[![Buy Me A Coffee](https://img.shields.io/badge/Buy%20Me%20A%20Coffee-ffdd00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/swartbergstudio)

</div>
