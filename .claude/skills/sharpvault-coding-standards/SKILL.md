---
name: sharpvault-coding-standards
description: SharpVault C# coding standards. Use whenever writing or changing any C#, Razor or project file in this repo, and when reviewing code. Covers no inline comments, naming, file layout, async, nullability, logging and security rules.
---

# SharpVault coding standards

These rules apply to every C#, Razor and project file. Reviewers reject code that breaks them.

## Comments

- **No inline comments.** Do not write `//` or `/* */` comments to explain what code does. Make the code explain itself with clear names, small methods and well-named types instead.
- **No commented-out code.** Delete it. Git keeps the history.
- **No file path comments** such as `// Application/Features/Secrets/CreateSecret.cs`.
- **No section divider comments** such as `// --- Helpers ---` or `#region`.
- **No TODO comments.** Create a GitHub issue or add the item to `docs/PROGRESS.md` instead.
- **Allowed:** the AGPL-3.0 file header from `.editorconfig`, and XML doc comments (`///`) on public types and members of shared libraries where they add information the name cannot carry.

If code feels like it needs a comment, rename or extract a method until it doesn't.

## Files and types

- One public type per file, file named after the type. The exception is the feature file that colocates a Request, its Handler and its Validator.
- File-scoped namespaces. Namespace matches the folder path.
- `using` directives outside the namespace, `System` first. Prefer global usings in a `GlobalUsings.cs` per project for namespaces used everywhere.
- Classes are `sealed` unless they are designed for inheritance.
- DTOs, requests and responses are `record`s.
- Prefer primary constructors for dependency injection.

## Naming

- PascalCase for types, methods, properties and constants. camelCase for locals and parameters. `_camelCase` for private fields.
- Interfaces start with `I`. Async methods end with `Async`.
- Names describe intent: `GetSecretsOwnedByUserAsync`, not `GetData`.
- No abbreviations except well-known ones (`Id`, `Dto`, `Api`, `Url`).

## Methods and control flow

- Keep methods short and focused on one thing.
- Return early with guard clauses instead of deep nesting.
- Always use braces, even for single-line `if` statements.
- Use `var` when the type is obvious from the right-hand side.
- Use expression-bodied members only when they fit on one line.
- Use pattern matching and switch expressions where they make the code clearer.
- No magic strings or numbers. Use constants, enums or options classes.

## Async

- Async all the way down. Never call `.Result`, `.Wait()` or `GetAwaiter().GetResult()`.
- Pass `CancellationToken` through every async call chain and accept it as the last parameter.
- No `async void` except Blazor event handlers that require it.

## Nullability and errors

- Nullable reference types are enabled and warnings are errors. Do not suppress warnings with `!` unless the value is guaranteed and there is no cleaner option.
- Handlers return `Result<T>` for expected failures. Throw exceptions only for truly exceptional situations.
- Validate input with FluentValidation in the pipeline, not inside handlers.

## Configuration

- Strongly typed options classes bound with `IOptions<T>` and validated on start (`ValidateOnStart()`).
- No secrets, keys or connection strings in `appsettings.json`. Use user-secrets locally and managed identity in Azure.

## Logging

- Use `ILogger<T>` with structured logging and message templates: `logger.LogInformation("Transferred {SecretCount} secrets to {TargetUserId}", count, targetId)`.
- Prefer the `LoggerMessage` source generator for logs on hot paths.
- **Never log secret values, data keys, decrypted content, tokens or full request bodies.**

## Security

- Every endpoint has an explicit authorization policy. Nothing is anonymous unless it is a health check.
- Every query that returns secrets filters by the current user or checks a share. Never trust ids from the client alone.
- Use `RandomNumberGenerator` for anything random that matters, never `Random`.
- Compare secrets and tokens with `CryptographicOperations.FixedTimeEquals`.
- Zero sensitive buffers with `CryptographicOperations.ZeroMemory` after use.

## Packages

- Add package versions only in `Directory.Packages.props`.
- Do not add a new package when the .NET base library already does the job.
