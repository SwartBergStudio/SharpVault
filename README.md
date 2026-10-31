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

</div>

---

> ⚠️ **Status: Early development.** SharpVault has not been independently security-audited. Please don't use it as your only store for important credentials yet.

## ✨ Why SharpVault?

SharpVault is a team password manager for organisations that already run on Microsoft Entra ID and Azure. It is built end to end in **C# and .NET**, so .NET developers can read, audit, extend and self-host every layer.

- **Sign in with your organisation account**: Microsoft Entra ID with app roles for admins and users.
- **Cheap to run**: secrets live in Azure Table Storage, accessed with Entra role-based access (no storage keys).
- **Your keys, your Key Vault**: every secret is encrypted with its own key, wrapped by a key in your Azure Key Vault.
- **Built for staff changes**: admins can transfer a leaving employee's secrets to someone else, with a full audit trail.
- **One codebase, every platform**: a shared Razor Class Library powers both the Blazor web app and the .NET MAUI apps.
- **Open source**: transparent code you can inspect and trust.

## 🧩 Features

### Core
- [ ] Encrypted vault for passwords, secure notes, API keys and other secrets
- [ ] Strong password and passphrase generator
- [ ] Folders, tags and fast metadata search
- [ ] Share a secret with another user (read or edit)

### Administration
- [ ] **Transfer secrets** from one user to another, for example when an employee leaves
- [ ] Resumable transfer jobs, so an interrupted transfer never loses or duplicates secrets
- [ ] Audit log of every reveal, share and transfer, with filtering
- [ ] User overview with secret counts

### Security
- [ ] AES-256-GCM encryption with a unique key per secret
- [ ] Data keys wrapped by Azure Key Vault (envelope encryption), with key rotation support
- [ ] Microsoft Entra ID sign-in with `SharpVault.Admin` and `SharpVault.User` app roles
- [ ] Managed identity access to Table Storage and Key Vault, no secrets in configuration
- [ ] Clipboard auto-clear and idle auto-lock

### Platforms
- [ ] Blazor web app
- [ ] .NET MAUI for Android, iOS, Windows and macOS

## 🔒 Security model

SharpVault uses **server-side envelope encryption**:

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
├── src/
│   ├── SharpVault.Domain/            # Entities and core rules
│   ├── SharpVault.Application/       # Use cases (CQRS requests and handlers)
│   ├── SharpVault.Infrastructure/    # Table Storage, Key Vault, encryption
│   ├── SharpVault.Api/               # ASP.NET Core minimal APIs
│   ├── SharpVault.UI.Shared/         # Razor Class Library (shared components)
│   ├── SharpVault.Web/               # Blazor web app
│   ├── SharpVault.Maui/              # .NET MAUI Blazor Hybrid app
│   ├── SharpVault.AppHost/           # Aspire orchestration for local development
│   └── SharpVault.ServiceDefaults/   # OpenTelemetry, health checks, resilience
└── tests/
    ├── SharpVault.UnitTests/
    └── SharpVault.IntegrationTests/
```

## 🛠️ Tech Stack

| Layer        | Technology                                          |
|--------------|-----------------------------------------------------|
| Language     | C# / .NET 10                                        |
| Web UI       | Blazor                                              |
| Mobile       | .NET MAUI Blazor Hybrid                             |
| API          | ASP.NET Core Minimal APIs, OpenAPI, Scalar          |
| Application  | SwartBerg.Mediator (CQRS), Mapster, FluentValidation |
| Storage      | Azure Table Storage (`Azure.Data.Tables`)           |
| Keys         | Azure Key Vault                                     |
| Auth         | Microsoft Entra ID (Microsoft.Identity.Web, MSAL)   |
| Local dev    | .NET Aspire with a persistent Azurite container     |
| Testing      | xUnit, Testcontainers (Azurite), AutoFixture        |

## 🚀 Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Aspire runs Azurite in a container)
- The .NET MAUI workload (for mobile): `dotnet workload install maui`
- An Entra ID tenant where you can create app registrations (see `docs/ENTRA-SETUP.md`)

### Clone and run

```bash
git clone https://github.com/SwartBergStudio/SharpVault.git
cd SharpVault
dotnet run --project src/SharpVault.AppHost
```

The Aspire dashboard opens with the API, the web app and Azurite running. Azurite uses a persistent container and data volume, so your local data survives between debug sessions.

### Run the tests

```bash
dotnet test
```

Integration tests start their own Azurite container with Testcontainers, so Docker must be running.

## 🗺️ Roadmap

- [ ] **v0.1**: Solution foundation, encryption, Table Storage, Entra sign-in
- [ ] **v0.2**: Secrets, sharing and password generator in the Blazor web app
- [ ] **v0.3**: Admin secret transfer and audit log
- [ ] **v0.4**: .NET MAUI apps
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
