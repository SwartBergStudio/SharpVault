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

</div>

---

> ⚠️ **Status: Early development.** SharpVault has not been independently security-audited. Please don't use it as your only store for important credentials yet.
>
> So far the repository contains the solution foundation: the build, local Aspire setup, storage registration, the API and web hosts without features, and CI. The features listed below are planned and not built yet.

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
- [ ] Encrypted vault for logins, secure notes, API keys and cards
- [ ] Folders and sub-folders, tags and search
- [ ] Strong password and passphrase generator
- [ ] Secret history with restore, and a recycle bin

### Sharing
- [ ] Share secrets or whole folders with colleagues in your Entra tenant (view, edit or manage)
- [ ] One-time share links for anyone, destroyed after one view or when they expire
- [ ] Each user sees only their own vault and what is shared with them

### Administration
- [ ] Lock users out instantly, or schedule a lock for their last day
- [ ] Transfer a user's secrets when they leave, are dismissed or pass away
- [ ] Every transfer needs a written reason and approval by a second admin
- [ ] Admins never see other users' secrets unless they receive them through an approved transfer
- [ ] Append-only audit log of every reveal, share, lock and transfer, with search and CSV export

### Security
- [ ] AES-256-GCM encryption with a unique key per secret, wrapped by your Azure Key Vault
- [ ] Microsoft Entra ID sign-in, limited to your tenant, with `SharpVault.Admin` and `SharpVault.User` roles
- [ ] Managed identity access to Table Storage and Key Vault, no secrets in configuration
- [ ] Clipboard auto-clear and idle auto-lock

### Platforms
- [ ] Blazor web app
- [ ] .NET MAUI for Android, iOS, Windows and macOS

### Later
- [ ] Import from Keeper, Bitwarden, 1Password and CSV
- [ ] TOTP codes, attachments and a password health report
- [ ] Browser extension and mobile autofill
- [ ] Sharing with Entra groups

## 🔒 Security model

SharpVault is designed to use **server-side envelope encryption**. The encryption code is not part of the foundation, so this section describes the planned design:

```
Secret value ──► AES-256-GCM (unique data key + nonce) ──► Encrypted value ──► Azure Table Storage
                         │
      Data key ──► wrapped by Key Vault key (RSA-OAEP-256) ──► Wrapped key stored with the secret
```

- Each secret has its own random 256-bit data key, and the secret id and owner are bound to the ciphertext.
- Data keys can only be unwrapped through your organisation's Azure Key Vault, using the app's managed identity.
- Secret values are decrypted only when a user explicitly reveals or copies them, and every reveal is audited.

**This is not a zero-knowledge design, on purpose.** Because admins must be able to transfer secrets when people leave, the application (through your Key Vault) can decrypt secrets. Whoever controls your Azure tenant and Key Vault controls access to the data, so protect those accordingly.

## 🏗️ Architecture

SharpVault follows **Clean Architecture**, with a shared UI layer so web and mobile stay in sync.

```
SharpVault/
├── SharpVault.slnx                   # Solution (Linux build and tests)
├── global.json, Directory.Build.props, Directory.Packages.props
├── src/
│   ├── SharpVault.Domain/            # Core rules (empty until features add entities)
│   ├── SharpVault.Application/       # CQRS via SwartBerg.Mediator, mapping, validation
│   ├── SharpVault.Infrastructure/    # Azure Table Storage registration
│   ├── SharpVault.Api/               # ASP.NET Core minimal API host
│   ├── SharpVault.UI.Shared/         # Razor Class Library (shared UI)
│   ├── SharpVault.Web/               # Blazor web app host
│   ├── SharpVault.Maui/              # .NET MAUI Blazor Hybrid app (planned in feature/maui-app)
│   ├── SharpVault.AppHost/           # Aspire orchestration for local development
│   └── SharpVault.ServiceDefaults/   # OpenTelemetry, health checks, security headers
├── tests/
│   ├── SharpVault.UnitTests/
│   ├── SharpVault.IntegrationTests/  # Api and Web hosts with Azurite (Testcontainers)
│   └── SharpVault.AppHost.Tests/     # Aspire AppHost model tests
├── eng/
│   └── CoverageGate.cs               # Coverage threshold check used in CI
├── docs/                             # Requirements, progress, local development, storage, testing
└── .github/                          # CI, CodeQL, dependency review and Dependabot
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
| Keys         | Azure Key Vault (planned)                           |
| Auth         | Microsoft Entra ID, Microsoft.Identity.Web (planned) |
| Local dev    | .NET Aspire with a persistent Azurite container     |
| Testing      | xUnit, Testcontainers (Azurite), AutoFixture        |

## 🚀 Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Aspire runs Azurite in a container, and the integration tests start one)
- An Entra ID tenant, needed once sign-in lands in `feature/entra-sign-in`. Nothing in the foundation needs one.
- The .NET MAUI workload is not needed yet, because the MAUI project is planned in `feature/maui-app`.

### Clone and run

```bash
git clone https://github.com/SwartBergStudio/SharpVault.git
cd SharpVault
dotnet run --project src/SharpVault.AppHost
```

The Aspire dashboard opens with the API, the web app and Azurite running. Azurite uses a persistent container and data volume, so your local data survives between debug sessions. For the HTTPS profile, run `dotnet dev-certs https --trust` if you get a certificate error.

The web app is a static shell without sign-in. The API answers only `/health` and `/alive` anonymously and returns 401 for every other route until sign-in lands. The OpenAPI document and Scalar are available in Development only.

See [docs/LOCAL-DEV.md](docs/LOCAL-DEV.md) for profiles, wiping local data and coverage commands.

### Run the tests

```bash
dotnet test SharpVault.slnx --filter "Category!=Performance"
```

Integration tests start their own Azurite container with Testcontainers, so Docker must be running. See [docs/TESTING.md](docs/TESTING.md) for the test projects and helpers.

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

</div>
