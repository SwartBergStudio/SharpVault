# Progress

This file tracks what is done and what comes next, so each work session can continue where the last one stopped.

## Done

- Repository setup: README, AGPL-3.0 license, CLAUDE.md conventions, CODEOWNERS, SECURITY.md,
  CONTRIBUTING.md, issue and pull request templates, .editorconfig with license header, .gitattributes.
- Claude setup: project skills in `.claude/skills/` and planner, implementer, test-writer, reviewer and docs-writer agents in `.claude/agents/`.

## Next

1. Solution scaffold on .NET 10: projects, Directory.Build.props, Directory.Packages.props, CI workflow.
2. Domain and Application layers with SwartBerg.Mediator.
3. Encryption (AES-256-GCM, IKeyWrapProvider with Key Vault and development providers) with unit tests.
4. Table Storage infrastructure and repositories.
5. API endpoints with Entra ID authentication and role policies.
6. Aspire AppHost with persistent Azurite.
7. Integration tests with Testcontainers.Azurite.
8. Admin secret transfer end to end.
9. Blazor UI in UI.Shared, Web host, then the MAUI scaffold.
