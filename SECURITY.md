# Security Policy

SharpVault stores passwords and secrets, so we take security reports seriously.

## Supported versions

SharpVault is in early development. Security fixes are applied to the latest code on `main` only.

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions or pull requests.**

Report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/SwartBergStudio/SharpVault/security/advisories/new).

Please include as much of the following as you can:

- The type of issue (for example, encryption weakness, authorization bypass, secret leakage in logs)
- The affected files, endpoints or components
- Steps to reproduce, or a proof of concept
- The impact, and how an attacker could exploit it

## What to expect

- We aim to acknowledge your report within 3 working days.
- We will keep you updated while we investigate and fix the issue.
- Once a fix is released, we are happy to credit you in the advisory unless you prefer to remain anonymous.

## Scope

In scope: encryption and key handling, authentication and authorization, the admin transfer feature,
audit logging, and any way secret values could be exposed.

Out of scope: issues that need a compromised Azure tenant or Key Vault, denial of service by volume,
and findings from automated scanners without a demonstrated impact.
