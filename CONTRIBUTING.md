# Contributing to SharpVault

Thanks for your interest in SharpVault! Bug reports, ideas and pull requests are all welcome.

## Before you start

- For anything bigger than a small fix, please open an issue first so we can agree on the approach.
- For security issues, do not open an issue. Follow [SECURITY.md](SECURITY.md) instead.

## Branches

Create your branch from `main` using this naming convention:

| Type of work | Branch name |
|---|---|
| New feature or general work | `feature/{short-description}` |
| Bug fix | `fix/{short-description}` |

Use lowercase kebab-case that describes the work, for example `feature/password-generator` or
`fix/transfer-resume-duplicates`. Random or generated branch names will not be accepted.

## Commits

Use [conventional commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `chore:`, `test:`, `docs:`, `refactor:`.

## Pull requests

- Open pull requests against `main`. Direct pushes to `main` are blocked.
- Every pull request needs a review from the code owner (@SwartBergStudio).
- Pull requests are squash merged, so the pull request title becomes the commit message on `main`.
  Write it as a conventional commit, for example `feat: add admin secret transfer`.
- Make sure the solution builds and `dotnet test` passes.
- Add or update tests for your change.

## Coding conventions

The project conventions (architecture, libraries, storage, security rules) are documented in
[CLAUDE.md](CLAUDE.md). Please follow them in your contributions.

## License

By contributing, you agree that your contributions will be licensed under the
[GNU Affero General Public License v3.0](LICENSE).
