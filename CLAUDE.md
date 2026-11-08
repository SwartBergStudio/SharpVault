# SharpVault

Open-source (AGPL-3.0) password and secrets manager for organisations, built with C# and .NET 10.
Owner: SwartBergStudio (@SwartBergStudio is code owner for every pull request).

## Git rules

- Do not add any attribution lines to commit messages or pull request descriptions:
  no `Co-Authored-By` trailer, no `Claude-Session` link, and no "Generated with Claude Code" footer.
- Branch names MUST be `feature/{short-description-of-the-work}` in lowercase kebab-case,
  for example `feature/table-storage-repositories` or `feature/admin-secret-transfer`.
  Never use random or generated branch names. Bug fixes use `fix/{description}`.
- Never commit directly to `main`. All work goes through a pull request to `main`.
- Use conventional commit messages: `feat:`, `fix:`, `chore:`, `test:`, `docs:`, `refactor:`.
- Keep commits small and logical. Make sure the solution builds and tests pass before pushing.

## How work is done: agents and models

Planning and review use the strongest model; writing code and tests uses the smallest model.
The main session orchestrates and delegates. It does not write production code itself.

| Agent | Model | Job |
|---|---|---|
| `planner` | Opus 5.5 | Writes the plan to `.github/spec/{feature-name}.md` |
| `implementer` | Haiku | Implements one batch of tasks. A fresh agent for each batch |
| `test-writer` | Haiku | Writes and runs the tests for one batch, after the code for it is done |
| `reviewer` | Opus 5.5 | Reviews the diff, builds and tests, approves or requests changes |
| `docs-writer` | Haiku | Updates `docs/` and the README |

Size the process to the work:

- **Small** (a bug fix, or a change to a few files): no planner and no spec. The main session briefs one `implementer`, or makes the fix itself when that is cheaper, then runs the affected tests. No docs agent unless behaviour in `docs/` changed.
- **Feature**: the full steps below.

For a feature:

1. Create the branch `feature/{feature-name}` from `main`.
2. Ask the `planner` for a plan of at most 150 lines. Read it and follow it.
3. Give each batch of tasks (about five files) to a fresh `implementer`, then its tests to a fresh `test-writer`. Do not resume an old agent with `SendMessage`: the plan on disk carries the state, and a long-lived agent gets more expensive with every turn. One agent runs at a time: no parallel agents and no per-agent worktrees.
4. Run the full suite once, then ask the `reviewer` to review the whole diff. Send blocking findings to a fresh `implementer` or `test-writer`. Fix them and re-check them by filter. Review once more only if the fixes were large.
5. Ask the `docs-writer` to update `docs/PROGRESS.md` and any affected docs.
6. Commit. Push and open a pull request only when the owner says so.

Do not pass a `model` override when delegating, because each agent's model is set in `.claude/agents/`. The exception: if the project agents are not loaded (the session started outside this folder, and you get `Agent type 'implementer' not found`), use `general-purpose` with `model: haiku` for the Haiku roles, and tell it to read its `.claude/agents/*.md` file first.

### Keeping the work moving and the cost down

- **Do not stop to ask about anything that can be changed later.** Work may be running with nobody at the keyboard, and an open question blocks everything behind it. Take the recommended default, record it in the plan as a default, not as an owner decision, list it in the final report, and keep going. Ask only about what is hard to reverse or goes outside this machine (pushing, deleting data). Even then, finish all the other work first.
- **Do not trust a dialog answer that arrives after a long wait.** The click that activates a terminal can answer an open dialog. Say what was selected and treat it as a default until the owner confirms it.
- **The requested work comes first.** A defect found on the way that does not block it gets written down in the spec and is fixed after the requested work, in the same session. Do not start a side fix ahead of the feature.
- **Test our logic, not the framework.** Do not test what .NET, Blazor, MudBlazor, System.Text.Json, Azure SDKs or AES-GCM already guarantee, such as Blazor encoding markup or AES-GCM detecting a flipped byte. Test that we use them correctly, once: that tampering is refused, or that a setting we chose is applied. Use a few rows per rule (each boundary and each distinct failure), not every possible value. Do not test that a constant equals its value. Style and convention checks are one test over all files, or an analyzer, not one case per file. Security rules we wrote still get full coverage: ownership, permissions, approvals, what gets logged, what is bound into encryption.
- **Tests:** while working, run only the affected tests, by filter. Run the full suite once at the end of the work. Use `-nr:false`, and no mutation runs unless the owner asks.
- **A failing test is a defect, not noise.** A flaky browser test gets its cause measured. Do not re-run it until it passes, and do not raise its timeout to hide it.
- **Report plainly** what ran, what passed, what did not, and what was not checked. Do not commit or push when the owner has said they will test first.

## Skills

The project skills in `.claude/skills/` describe how we work. Use them instead of similarly named personal skills.

## Definition of done

Work is only done when all of these are true:

- It follows the coding standards in `sharpvault-coding-standards` (no inline comments, no commented-out code, no TODOs).
- The plan has a Threats section, and every threat has a passing test.
- Every endpoint has the full authorization matrix test.
- Edge cases and every failure path are tested; every bug fix has a regression test.
- Performance budgets and storage call counts are tested; no query runs without a partition key.
- `dotnet build` has no warnings, and all tests, coverage thresholds and the vulnerable package check pass.
- The `reviewer` has approved.

## Tech stack

- Clean Architecture: Domain, Application, Infrastructure, Api, Dtos (API contracts), UI.Shared (Razor Class Library),
  Web (Blazor Web App), Maui (.NET MAUI Blazor Hybrid), AppHost and ServiceDefaults (Aspire).
- Lean domain models: domain methods are the exception, not the rule.
- CQRS via SwartBerg.Mediator (namespace `Mediator`, MediatR-compatible interfaces).
  Each Request and its Handler live together in one file per feature.
- Mapster for DTO mapping, FluentValidation for request validation.
- DTOs: every DTO lives in `SharpVault.Dtos`. A DTO is a `sealed` class with public `{ get; set; }` properties and no logic (no records, no methods, no framework types). Handlers return `Result<SomeDto>`. Mapster mapping stays in Application. The UI references only `SharpVault.Dtos` for API types.
- Minimal APIs grouped by feature, with OpenAPI and Scalar.
- UI is Blazor and Razor only. No React, Vue or JavaScript frameworks. Blazor JS interop (`IJSRuntime.InvokeAsync`) is allowed
  for specific browser methods that .NET cannot call directly (for example `navigator.clipboard.writeText`, or Web Crypto for one-time links),
  wrapped behind a C# interface and kept to small, focused modules.
  All UI lives in `SharpVault.UI.Shared` so Web and MAUI share it.
  The UI is built on MudBlazor 9.12.0, used only through the `Sv*` wrappers in `Components/Common`. Pages only route and compose, with no code beyond parameter wiring. Feature components live in `Components/{Feature}`.
  Every interactive element gets a stable `id` attribute for testing. Wrappers check their ids (lowercase kebab-case).
  Admin decisions in the UI read `IsAdmin` from `GET /api/me`. The API is the security boundary.
  The Web's Content Security Policy allows inline styles only through `style-src 'unsafe-inline'`, because MudBlazor sets style attributes at runtime. Scripts stay `'self'`, and our markup has no `style` attributes and no inline script.
- Every `.cs` file starts with the AGPL-3.0 header defined in `.editorconfig`.

## Storage

- Azure Table Storage through `Azure.Data.Tables`. No SQL database and no EF Core.
- Production authenticates with Microsoft Entra ID through `DefaultAzureCredential` (managed identity).
  No account keys or SAS tokens. The app identity needs "Storage Table Data Contributor".
- Use ETags for optimistic concurrency on every update. Storage sits behind store interfaces (see `sharpvault-table-storage`).

## Authentication and authorization

- Microsoft Entra ID with Microsoft.Identity.Web. App roles: `SharpVault.Admin` and `SharpVault.User`.
- Users are identified by their Entra object ID (`oid` claim), never by email.

## Encryption

- Server-side envelope encryption, NOT zero-knowledge (admins must be able to transfer secrets).
- Each secret value: AES-256-GCM with its own random data key and a unique 96-bit nonce,
  with secret id and owner id bound as associated data.
- Data keys are wrapped by a key-encryption key in Azure Key Vault, behind `IKeyWrapProvider`.
  The development key provider must refuse to run outside the Development environment.
- Never log secret values, data keys or decrypted content. Audit every reveal and every transfer.

## Product requirements

`docs/REQUIREMENTS.md` is the product specification. Plan every feature against it and reference its requirement ids
(for example `TRF-3`) in plans, tests and pull requests. The rules that matter most:

- Single Entra tenant per deployment. Only members of that tenant can sign in or receive shares.
- A user sees only their own vault plus what is shared with them. Admin rights never grant access to other users' secrets.
- Admins can lock users. Lock status is checked on every request.
- Transfers (left the company, dismissed, deceased, role change) need a reason and approval by a second admin.
  The target user sees nothing until the transfer is approved and completed.
- One-time share links open once for anyone with the link. The link key lives in the URL fragment and decryption happens
  in the browser (Web Crypto through Blazor JS interop), so the server cannot read link contents.
- Every lock, unlock, transfer step, reveal and share is written to the append-only audit log, never with secret values.

## Local development and testing

- `SharpVault.AppHost` (Aspire) runs everything, with Azurite as a persistent container
  (`WithDataVolume()` and `ContainerLifetime.Persistent`).
- Unit tests with xUnit. Integration tests with WebApplicationFactory, Testcontainers.Azurite and AutoFixture.
- Everything except `SharpVault.Maui` must build and pass `dotnet test` on Linux.
- In cloud sessions, check `dotnet --info` first and make sure the Docker daemon is running before integration tests.

## Progress

Keep `docs/PROGRESS.md` up to date with what is done, in progress and next, so the next session can continue.
