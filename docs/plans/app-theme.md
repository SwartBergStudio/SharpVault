# Plan: app theme ("Vault Steel")

- **Branch**: `feature/app-theme` (already created from `main`, which holds the foundation and the encryption core)
- **PROGRESS.md item**: new. It goes before item 4 (`feature/vault-and-folders`), because the vault UI is the first big user of the theme. It resolves foundation decision D20 (theme and fonts deferred).
- **Prerequisite (T0)**: `feature/entra-sign-in` must be merged into `main`, and `main` merged into this branch, **before T5, T6, T8 and T9** start. T1 to T4 and T7's non-layout tests do not touch any file that entra-sign-in changes, so they can start now.
- **Planner verification**: items marked "verified" were checked in this session on .NET SDK 10.0.114 (Linux) in a throwaway copy of the repository in the planner scratchpad, or by downloading and hashing the packages. Implementers can rely on them.
- **Foundation conventions still apply**: AGPL header on every `.cs` file; no comments of any kind in `.cs`, `.razor`, `.css` or `.js` files (no `/* */` in CSS either), no `#region`, TODOs or commented-out code; classes `sealed`, abstract or static (Razor components are exempt, `LayeringTests` already excludes `ComponentBase` types); no type name ends in `Collection` (CA1711); xUnit1051 needs `TestContext.Current.CancellationToken`; enum defaults fail safe (value 0 is the most cautious state).

## 1. Goal

A distinctive security and vault identity for every SharpVault screen, built once in `SharpVault.UI.Shared` so the Web and MAUI hosts share it. The owner's direction: "the app theme should be all about security and vault."

The concept, **Vault Steel**: a machined vault at night. Surfaces are blued graphite and deep navy like gun-blued steel. The header has a faint brushed-metal grain and a thin brass rim. Labels look like engraved plates: wide, tracked capitals. Brass is the single accent and is used only where a vault door has polished parts: the handle wheel, the primary action, the focus ring, the current location and a revealed secret. Secret values sit in sunken wells and use a monospace face with ligatures turned off. The memorable moment is the vault-door mark: its handle wheel turns and its bolts engage once when you sign out ("the vault is sealed"), and nowhere else unless it means something.

Deliverables:

- **Fonts**: two self-hosted OFL variable fonts (Archivo for UI and display, JetBrains Mono for secrets and identifiers), pinned by size and SHA-256, with their licences.
- **Design tokens** as CSS custom properties for dark (default) and light schemes, every text and UI pair meeting WCAG 2.2 AA, proven by a test that computes the ratios from the stylesheet.
- **One global stylesheet** (`app.css`) with cascade layers: reset, base elements, components (buttons, inputs, fields, cards, tables, lists, tags, status badges, alerts, dialogs, secret frame, strength meter, navigation, folder tree, empty and state panels, hero, lock screen, busy indicator), layout shell, utilities, forced colours, reduced motion and print.
- **Razor components**: `Icon` (31 inline SVG icons), `VaultDoor` (brand mark and hero illustration with the sealing animation), `StatusBadge`, `Alert`, `EmptyState`, `DialogShell`, `SecretFrame`, `BusyIndicator` and the `ComponentIds` guard.
- **Layout shell**: skip link, sticky header with brand and account menu, side navigation (horizontal strip on narrow screens), main landmark, themed Blazor error bar, safe-area padding for MAUI.
- **Themed pages**: Home, NotFound, AccessDenied, SignedOut, SignInError, Account (from entra-sign-in), the Web `Error` page, the Routes "not authorized" message and the Web reconnect modal.
- **Web host head**: `viewport-fit=cover`, `color-scheme` and `theme-color` metadata, and an SVG favicon (also removes a `/favicon.ico` 404 render on every first visit).
- **Guards**: source scans, rendered-HTML checks, bUnit tests, contrast tests and size budgets that keep the strict CSP (no inline styles, no inline scripts, no external origins) and the security visuals intact for every later feature.

### Out of scope

- A user-selectable light or dark toggle. The theme follows the operating system (`prefers-color-scheme`), D2.
- Idle auto-lock behaviour (ADM-4). This work only ships the lock-screen visuals and their contract (4.15).
- The vault, sharing, lock and admin components themselves. This work gives them the building blocks and the contract in 4.15.
- `SharpVault.Maui` and its `wwwroot/index.html` (`feature/maui-app`). This work makes the CSS MAUI-ready (4.13).
- Playwright E2E and automated accessibility scans in a browser (foundation D23). bUnit, HTML and CSS tests cover what can be checked without a browser.
- Theming Scalar, the Aspire dashboard or Entra's hosted sign-in pages.
- Changing the CSP (D8).

## 2. Requirements

There is no product requirement for the theme itself (UX foundation). It supports these:

| Id | How this work supports it |
|---|---|
| SEC-3 | Hidden by default visuals: `SecretFrame` defaults to hidden, renders a fixed-length mask and never its content while hidden, shows a distinct "Visible" state with an eye icon, text and a brass frame when revealed, opts the value out of browser translation, and print hides revealed values. |
| ADM-4 (groundwork) | Opaque lock-screen styles (`.sv-lockscreen`) and the sealing vault door for the idle auto-lock, plus the contract in 4.15. |
| LCK-1 (visuals) | `StatusBadge` with distinct icon and text for Locked, Active, Warning (for example "Lock scheduled"), Danger and Unknown, never colour alone. |
| OTL-3 (baseline) | The strict CSP that one-time links depend on is kept: no inline style, no inline script, no external origin, fonts self-hosted. Tests fail any change that would need the CSP to be loosened. |

Pull requests and tests reference these ids. The pull request says "UX foundation (no requirement id); supports SEC-3, ADM-4, LCK-1, OTL-3".

## 3. Verified facts

| # | Fact | How |
|---|---|---|
| V1 | The Web host serves a font placed in `src/SharpVault.UI.Shared/wwwroot/fonts/` at `/_content/SharpVault.UI.Shared/fonts/{file}` with `200`, `Content-Type: font/woff2`, `X-Content-Type-Options: nosniff`, `Cross-Origin-Resource-Policy: same-origin`, the full web CSP, and (Development) `Cache-Control: no-cache` with an ETag. The security headers middleware does not add `no-store`, because the static asset handler already sets `Cache-Control`. | Ran the Web host from a scratch copy, `curl -D -` |
| V2 | `url("../fonts/x.woff2")` inside `app.css` is **not** rewritten to the fingerprinted name. The static web assets manifest has both `fonts/x.{fingerprint}.woff2` (`max-age=31536000, immutable`) and `fonts/x.woff2` (`max-age=3600, must-revalidate` in published builds). CSS therefore loads the non-fingerprinted path, which is revalidated hourly in production. | Built output, `*.staticwebassets.endpoints.json` |
| V3 | `@Assets["_content/SharpVault.UI.Shared/css/app.css"]` renders a fingerprinted href (`app.kgjmld5x1y.css`). | Rendered `/` |
| V4 | `@fontsource-variable/archivo` 5.3.0 (tarball integrity `sha512-HogK8FJelrD1o7TlZlkIVtHgc20bO5PZRWE7mUeUTdMN055alznQV6/00J00IBeu8FQAH4s3zW9UJNvKExXf+g==`) and `@fontsource-variable/jetbrains-mono` 5.3.0 (`sha512-F32xpS2NsGYoQi2ADSkKTgpJj7ozajsGgDJ8woTnqjmIB+dxDIqImjl4pXZVEExu8UFZ2ndhmX18EBS/hdz3Lw==`) are reachable at `registry.npmjs.org` (jsDelivr and GitHub releases are blocked). Both are `OFL-1.1`. Their `LICENSE` files contain the full OFL text and declare **no Reserved Font Name**. The files are real WOFF2 (`wOF2` signature). File sizes and SHA-256 are in 4.2. | Downloaded, hashed, checked integrity |
| V5 | Archivo's `latin-wdth-normal` file carries both the `wdth` (62 to 125) and `wght` (100 to 900) axes and is byte-identical to `latin-standard-normal`. JetBrains Mono carries `wght` 100 to 800. Fontsource's unicode ranges: latin `U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD` (includes U+2022, the mask bullet); latin-ext `U+0100-02BA,U+02BD-02C5,U+02C7-02CC,U+02CE-02D7,U+02DD-02FF,U+0304,U+0308,U+0329,U+1D00-1DBF,U+1E00-1E9F,U+1EF2-1EFF,U+2020,U+20A0-20AB,U+20AD-20C0,U+2113,U+2C60-2C7F,U+A720-A7FF`. | Package `metadata.json` and CSS |
| V6 | `lucide-static` 1.66.0 (integrity `sha512-j8Bjf/f21PtlD8p0wx1lRxyOrs98WZ1QEImkWmfskuJ8mOCwTN7Dwc6EHUzJDSzh79aB+Bo7SWcoTCM2zEjnJg==`) is ISC licensed, with an MIT notice for icons derived from Feather. Its `LICENSE` is 3,208 bytes, SHA-256 `b495047bd93a9b06913511076f504daba17d5bbeb3e0650f3bb53a4220329c57`. The inner SVG elements of every icon used are in 4.8. | Downloaded |
| V7 | Every contrast pair in 4.4 meets its minimum in both schemes, using the WCAG 2.x relative luminance formula. The lowest are 3.53 (light `border-strong` on `surface-sunken`, minimum 3.0) and 4.10 (dark `danger-solid` boundary on `surface-raised`, minimum 3.0). The lowest text pair is 5.02 (dark `on-danger` on `danger-solid`). | Python in the scratchpad |
| V8 | `blazor.web.js` 10.0.14 binds the error UI with `document.querySelectorAll("#blazor-error-ui .dismiss")` and `.reload`, any element type, and shows it with `style.display = "block"` (CSSOM, which the CSP allows). A `<button class="dismiss">` therefore works. | Read the shipped script |
| V9 | `bunit` 2.13.3 is in the package cache (the vault plan verified it with `xunit.v3.mtp-off` 4.0.2) and exposes `BunitContext`, `Render<T>`, `AddAuthorization()` returning `BunitAuthorizationContext.SetAuthorized(...)`, `JSInterop.Mode = JSRuntimeMode.Loose`, `JSInterop.VerifyFocusAsyncInvoke()` and `ShouldBeElementReferenceTo(...)`. | Package XML docs |
| V10 | `Microsoft.AspNetCore.Components.Web` 10.0.14 contains the string `aria-current` (NavLink sets `aria-current="page"` on the active link). The CSS styles both `.active` and `[aria-current="page"]` so it does not depend on this. | Binary string search |
| V11 | On `feature/entra-sign-in`: `app.MapStaticAssets().AllowAnonymous()` (static assets bypass the fallback policy), `SecurityHeadersPolicy.ForWeb(Uri identityProviderOrigin)` replaces `ForWeb()`, `app.css` is unchanged from `main`, and tests assert `id="account-display-name"[^>]*>Ada Lovelace<` (the element with the id must contain the text directly). Anonymous pages `/`, `/account/access-denied`, `/account/signed-out`, `/account/sign-in-error` return 200. | Source read (read-only) |

## 4. Design

### 4.1 Where things live

```
src/SharpVault.UI.Shared/
  wwwroot/
    css/app.css                       the whole theme (4.5)
    fonts/                            4 woff2 files + 2 licence files (4.2)
    brand/favicon.svg                 (4.9)
  Components/
    ComponentIds.cs                   id guard (4.7)
    Icons/IconName.cs, Icon.razor     (4.8)
    Brand/VaultDoorVariant.cs, VaultDoor.razor   (4.9)
    Common/SecurityStatus.cs, StatusBadge.razor
    Common/AlertKind.cs, Alert.razor
    Common/EmptyState.razor, DialogShell.razor, SecretFrame.razor, BusyIndicator.razor
  Layout/
    MainLayout.razor, SkipLink.razor, AppHeader.razor, SideNav.razor, AccountMenu.razor
    (MainLayout.razor.css and AccountMenu.razor.css are deleted)
  Pages/ Home, NotFound, AccessDenied, SignedOut, SignInError, Account (restyled)
  _Imports.razor                      + Components.Icons, Components.Brand, Components.Common
src/SharpVault.Web/
  Components/App.razor                head metadata and favicon (4.10)
  Components/Routes.razor             NotAuthorized as a state panel
  Components/Pages/Error.razor        state panel
  Components/Layout/ReconnectModal.razor(.css)   tokens instead of hard-coded colours
THIRD-PARTY-NOTICES.md                fonts and icons
```

Namespaces follow folders: `SharpVault.UI.Shared.Components`, `.Components.Icons`, `.Components.Brand`, `.Components.Common`.

### 4.2 Typography and fonts (D1)

| Role | Family | File | Bytes | SHA-256 |
|---|---|---|---|---|
| UI, body, headings, plates (latin) | Archivo variable, `wdth` 62-125, `wght` 100-900 | `archivo-latin-wdth-normal.woff2` | 90,104 | `e3a28eade21a900c7155a247757f4b2834c07bb7ef07ad7efa55cebaac1e8f5e` |
| same, latin-ext (only downloaded when such characters appear) | Archivo | `archivo-latin-ext-wdth-normal.woff2` | 86,240 | `5717f37059660ca5c899bad6c48ee22c3ac55cb3c484055241689d0f905a1a86` |
| Secrets, ids, code (latin) | JetBrains Mono variable, `wght` 100-800 | `jetbrains-mono-latin-wght-normal.woff2` | 40,404 | `18be452724bfdc236c074ca94a249a7f41a86752c7d04ab258ce9ed5651f6a7e` |
| same, latin-ext | JetBrains Mono | `jetbrains-mono-latin-ext-wght-normal.woff2` | 15,196 | `79bfdab9ba467e26eea4122e6f2567e188dd8a09a8c730d501fc487c4ab99c6e` |
| Licence | OFL 1.1, Archivo | `OFL-Archivo.txt` (the package `LICENSE`, unmodified) | 4,503 | `c3c8402eec0e31dc9a76851235b1c02a0bb821488b2db8e35bcc2ab60ca8fc2b` |
| Licence | OFL 1.1, JetBrains Mono | `OFL-JetBrainsMono.txt` (the package `LICENSE`, unmodified) | 4,524 | `403581b69dac5cff4079205e01c6b467e56af449ecbd7247693ddb1baafa005b` |

Files are taken unmodified from `package/files/` of the npm tarballs in V4. No italics are shipped. Emphasis uses weight, never slant, and `font-synthesis: none` stops faux italics.

`@font-face` rules (four, top of `app.css`, after the `@layer` statement):

| `font-family` | `src` | `font-weight` | `font-stretch` | `unicode-range` |
|---|---|---|---|---|
| `"Archivo"` | `url("../fonts/archivo-latin-wdth-normal.woff2") format("woff2")` | `100 900` | `62% 125%` | latin (V5) |
| `"Archivo"` | `url("../fonts/archivo-latin-ext-wdth-normal.woff2") format("woff2")` | `100 900` | `62% 125%` | latin-ext (V5) |
| `"JetBrains Mono"` | `url("../fonts/jetbrains-mono-latin-wght-normal.woff2") format("woff2")` | `100 800` | (omit) | latin |
| `"JetBrains Mono"` | `url("../fonts/jetbrains-mono-latin-ext-wght-normal.woff2") format("woff2")` | `100 800` | (omit) | latin-ext |

Every rule has `font-style: normal` and `font-display: swap`. No `local()` source (a locally installed version could render differently, and it is a fingerprinting surface). No `<link rel="preload">` (D1).

Type usage:

| Use | Family | Size token | Weight | `font-stretch` | Other |
|---|---|---|---|---|---|
| Body | sans | `--text-md` | 400 | `--stretch-body` (100%) | `line-height: var(--leading-body)` |
| h1 | sans | `--text-3xl` | 700 | `--stretch-heading` (112%) | `line-height: var(--leading-tight)`, `letter-spacing: -0.01em` |
| h2 | sans | `--text-xl` | 650 | 108% | tight |
| h3 | sans | `--text-lg` | 600 | 104% | tight |
| Plate label (`.sv-plate`, table headers, `dt`, status text) | sans | `--text-xs` | 600 | `--stretch-plate` (125%) | `text-transform: uppercase`, `letter-spacing: var(--tracking-plate)`, colour `--color-text-muted` |
| Brand wordmark | sans | `--text-sm` | 700 | 125% | uppercase, `letter-spacing: 0.12em` |
| Hero title | sans | `--text-3xl` | 700 | 125% | uppercase, `letter-spacing: 0.06em` |
| Buttons | sans | `--text-sm` | 600 | 108% | sentence case, `letter-spacing: 0.01em` |
| Secrets, ids, `code`, `kbd`, `samp`, `pre` | mono | inherit (`--text-md` for secrets) | 450 | n/a | `font-variant-ligatures: none` and `font-feature-settings: "calt" 0, "liga" 0` (JetBrains Mono's coding ligatures would merge characters such as `!=` or `->` inside a password), `letter-spacing: 0.04em` for secrets |
| Tables | sans | `--text-sm` | 400 | 100% | `font-variant-numeric: tabular-nums` |

### 4.3 Design tokens

All tokens live on `:root` (dark, the default) and colour and shadow tokens are overridden in `@media (prefers-color-scheme: light) { :root { ... } }`. The names of the existing tokens (`--color-bg`, `--color-surface`, `--color-text`, `--color-text-muted`, `--color-accent`, `--color-on-accent`, `--color-border`, `--space-xs` to `--space-xl`, `--radius-sm` to `--radius-lg`) are kept, so code written against them keeps working (the vault plan refers to "the existing CSS variables"). `--color-error-bg`, `--color-error-text` and `--color-shadow` are removed (only `app.css` used them).

Every `--color-*` token is an opaque `#RRGGBB` hex, except names ending in `-grain` or `-overlay`, which are `rgba(...)`. Shadows are `--shadow-*`.

**Colour tokens**

| Token | Dark (`:root`) | Light (media block) | Used for |
|---|---|---|---|
| `--color-bg` | `#0A1017` | `#ECEFF3` | page |
| `--color-bg-glow` | `#13202E` | `#FFFFFF` | the radial "vault interior" light at the top of the frame |
| `--color-surface` | `#111923` | `#F8FAFC` | header, side nav, cards, lists, tables |
| `--color-surface-raised` | `#172230` | `#FFFFFF` | dialogs, hovered rows, current nav item, buttons |
| `--color-surface-sunken` | `#070B10` | `#E2E7ED` | inputs, secret wells, tags |
| `--color-border` | `#233140` | `#CBD3DC` | decorative dividers only |
| `--color-border-strong` | `#6B7E93` | `#6A7A8D` | input and control boundaries (3:1) |
| `--color-rivet` | `#3A4A5C` | `#B5C0CC` | decorative rivets on plate cards |
| `--color-text` | `#E7EBF0` | `#0E1620` | text |
| `--color-text-muted` | `#9AA7B6` | `#4B5869` | secondary text, plates, placeholders |
| `--color-accent` | `#D9AE55` | `#7D5A0F` | brass: primary button, current marker, revealed frame, wheel, link underline |
| `--color-accent-hover` | `#E8C477` | `#664909` | primary button hover |
| `--color-on-accent` | `#17110A` | `#FFFFFF` | text on brass |
| `--color-accent-soft` | `#2A2212` | `#F6EBD3` | revealed secret well, accent tag |
| `--color-focus` | `#F0C66B` | `#7D5A0F` | focus ring |
| `--color-success` | `#5BD69A` | `#146C43` | Active, success |
| `--color-success-bg` | `#0E2A1E` | `#E3F4EA` | |
| `--color-success-border` | `#1F5E41` | `#8CCBA9` | |
| `--color-locked` | `#8DBDF0` | `#1F5A94` | Locked (cold steel blue) |
| `--color-locked-bg` | `#0F2236` | `#E2EDF8` | |
| `--color-locked-border` | `#2A4E73` | `#9DBFE3` | |
| `--color-warning` | `#FF9D57` | `#9A4300` | Warning, scheduled lock (hazard orange, distinct from brass by hue and saturation) |
| `--color-warning-bg` | `#2E1A0C` | `#FCEBDD` | |
| `--color-warning-border` | `#7A4219` | `#E8B48B` | |
| `--color-danger` | `#FF7A7A` | `#B3242B` | errors, destructive outline |
| `--color-danger-bg` | `#321315` | `#FBE5E6` | |
| `--color-danger-border` | `#7D2A2E` | `#E9A3A6` | |
| `--color-danger-solid` | `#E5484D` | `#B3242B` | filled destructive confirm |
| `--color-danger-solid-hover` | `#F06A6E` | `#962027` | |
| `--color-on-danger` | `#1A0507` | `#FFFFFF` | text on `danger-solid` |
| `--color-grain` | `rgba(255, 255, 255, 0.025)` | `rgba(14, 22, 32, 0.03)` | brushed-metal hairlines |
| `--color-overlay` | `rgba(3, 6, 10, 0.72)` | `rgba(14, 22, 32, 0.45)` | dialog backdrop |
| `--shadow-raised` | `inset 0 1px 0 rgba(255, 255, 255, 0.04), 0 8px 24px rgba(0, 0, 0, 0.35)` | `inset 0 1px 0 rgba(255, 255, 255, 0.8), 0 6px 18px rgba(14, 22, 32, 0.1)` | cards |
| `--shadow-dialog` | `0 24px 64px rgba(0, 0, 0, 0.6)` | `0 24px 64px rgba(14, 22, 32, 0.25)` | dialogs, error bar |

`:root` also sets `color-scheme: dark`; the light block sets `color-scheme: light`.

**Other tokens (dark `:root` only)**

| Group | Tokens |
|---|---|
| Fonts | `--font-sans: "Archivo", ui-sans-serif, system-ui, sans-serif`; `--font-mono: "JetBrains Mono", ui-monospace, "Cascadia Mono", Menlo, Consolas, monospace` |
| Text sizes | `--text-xs: 0.75rem`, `--text-sm: 0.875rem`, `--text-md: 1rem`, `--text-lg: 1.125rem`, `--text-xl: 1.375rem`, `--text-2xl: 1.75rem`, `--text-3xl: clamp(2rem, 1.25rem + 2.5vw, 3rem)` |
| Line height | `--leading-tight: 1.15`, `--leading-body: 1.55` |
| Stretch and tracking | `--stretch-body: 100%`, `--stretch-heading: 112%`, `--stretch-plate: 125%`, `--tracking-plate: 0.14em` |
| Spacing | `--space-xs: 0.25rem`, `--space-sm: 0.5rem`, `--space-md: 1rem`, `--space-lg: 1.5rem`, `--space-xl: 2.5rem`, `--space-2xl: 4rem` |
| Radii (machined, small) | `--radius-sm: 3px`, `--radius-md: 6px`, `--radius-lg: 10px`, `--radius-round: 999px` |
| Motion | `--duration-fast: 120ms`, `--duration-normal: 200ms`, `--duration-seal: 700ms`, `--ease-out: cubic-bezier(0.2, 0, 0, 1)`, `--ease-lock: cubic-bezier(0.65, 0, 0.35, 1)` |
| Layout | `--header-height: 3.5rem`, `--sidenav-width: 15rem`, `--content-max: 72rem`, `--control-height: 2.5rem`, `--tap-target: 2.75rem` |
| Layers | `--z-header: 10`, `--z-dialog: 100`, `--z-lock: 900`, `--z-error-ui: 1000`, `--z-skip: 1100` |

Breakpoints (media queries cannot use variables, so these are fixed numbers): `30rem` (small phone), `40rem` (dialogs become bottom sheets below this), `48rem` (hero two columns), `60rem` (side nav column).

### 4.4 Contrast pairs (tested in both schemes)

Ratios use WCAG 2.x relative luminance (sRGB channel `c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ^ 2.4`, `L = 0.2126 R + 0.7152 G + 0.0722 B`, ratio `(L1 + 0.05) / (L2 + 0.05)`).

| Group | Foreground tokens | Background tokens | Minimum |
|---|---|---|---|
| Text | `text`, `text-muted` | `bg`, `surface`, `surface-raised`, `surface-sunken`, `accent-soft` | 4.5 |
| Accent as text | `accent` | `bg`, `surface`, `surface-raised`, `surface-sunken`, `accent-soft` | 4.5 |
| On fills | `on-accent` | `accent`, `accent-hover` | 4.5 |
| On fills | `on-danger` | `danger-solid`, `danger-solid-hover` | 4.5 |
| Status text and icons | each of `success`, `locked`, `warning`, `danger` | its own `-bg`, `surface`, `surface-raised`, `bg` | 4.5 |
| Text in status containers | `text` | `success-bg`, `locked-bg`, `warning-bg`, `danger-bg` | 4.5 |
| Control boundaries (1.4.11) | `border-strong` | `bg`, `surface`, `surface-raised`, `surface-sunken` | 3.0 |
| Focus ring (1.4.11, 2.4.13) | `focus` | `bg`, `surface`, `surface-raised`, `surface-sunken` | 3.0 |
| Filled control boundaries | `accent` on `surface`; `danger-solid` on `surface` and `surface-raised` | | 3.0 |

That is 50 pairs per scheme, 100 test cases. All pass (V7). `--color-border` is decorative and never the only boundary of a control, so it is not tested. Disabled controls are exempt (WCAG).

### 4.5 Stylesheet structure

`src/SharpVault.UI.Shared/wwwroot/css/app.css`, in exactly this top-level order (the contrast test parser depends on items 3 and 4):

1. `@layer sv-reset, sv-base, sv-components, sv-layout, sv-utilities;`
2. The four `@font-face` rules (4.2).
3. `:root { ... }` with every token (4.3, dark values). This is the **first** top-level rule whose selector is exactly `:root`.
4. `@media (prefers-color-scheme: light) { :root { ... } }` with only `color-scheme`, `--color-*` and `--shadow-*`.
5. `@layer sv-reset { ... }`
6. `@layer sv-base { ... }`
7. The four `@keyframes` (4.12), top level.
8. `@layer sv-components { ... }`
9. `@layer sv-layout { ... }`
10. `@layer sv-utilities { ... }` (utilities, forced colours, print)

Why layers (D4): unlayered styles beat layered ones, so a later feature's `.razor.css` (unlayered) always overrides a theme default without specificity fights, and the reduced-motion rule uses a layered `!important`, which beats every unlayered normal declaration, including future component animations.

Rules for every `.css` file under `src/SharpVault.UI.Shared` and `src/SharpVault.Web` (enforced by 7.1 scans): no comments; no `@import`; the only `url(` values are `url("../fonts/{name}.woff2")` in `app.css`; no `http:`, `https:`, `//` or `data:` inside `url(`; no `expression(`, `-moz-binding` or `behavior:`; no `-webkit-text-security`; no `content: attr(`; no attribute selector on `value` (`[value`); `infinite` only in `app.css` (`.sv-busy__pointer`) and `ReconnectModal.razor.css`.

### 4.6 Styles by layer

Values reference the tokens in 4.3. "fast" means `var(--duration-fast) var(--ease-out)`.

**`sv-reset`**

- `*, *::before, *::after`: `box-sizing: border-box; margin: 0; padding: 0;`
- `html`: `-webkit-text-size-adjust: 100%; text-size-adjust: 100%;`
- `html, body`: `min-height: 100%;`
- `img, svg`: `display: block; max-inline-size: 100%;`
- `button, input, select, textarea`: `font: inherit; color: inherit;`
- `[hidden]`: `display: none !important;`
- `@media (prefers-reduced-motion: reduce) { *, *::before, *::after { animation-duration: 0.01ms !important; animation-iteration-count: 1 !important; animation-delay: 0ms !important; transition-duration: 0.01ms !important; transition-delay: 0ms !important; scroll-behavior: auto !important; } }` (D7)

**`sv-base`**

- `body`: `background: var(--color-bg); color: var(--color-text); font-family: var(--font-sans); font-size: var(--text-md); font-stretch: var(--stretch-body); line-height: var(--leading-body); font-synthesis: none; text-rendering: optimizeLegibility; -webkit-font-smoothing: antialiased; -webkit-tap-highlight-color: transparent; scrollbar-color: var(--color-border-strong) transparent;`
- `h1, h2, h3`: per 4.2. `h1 { margin-bottom: var(--space-sm); }`. `p + p { margin-top: var(--space-sm); }`. `strong, b { font-weight: 650; }`. `em, i { font-style: normal; font-weight: 600; }`.
- `a`: `color: var(--color-text); text-decoration: underline; text-decoration-color: var(--color-accent); text-decoration-thickness: 1px; text-underline-offset: 0.2em; transition: text-decoration-thickness fast;` `a:hover { text-decoration-thickness: 2px; }` (the underline, not colour, distinguishes links).
- `:focus-visible`: `outline: 2px solid var(--color-focus); outline-offset: 2px;`
- `::selection`: `background: var(--color-accent); color: var(--color-on-accent);`
- `code, kbd, samp, pre`: mono family with the ligature settings in 4.2. `kbd { padding: 0 0.3em; border: 1px solid var(--color-border-strong); border-radius: var(--radius-sm); background: var(--color-surface-sunken); }`
- `hr`: `border: 0; border-top: 1px solid var(--color-border);`
- Text inputs, `input:not([type="checkbox"]):not([type="radio"]):not([type="hidden"]):not([type="submit"]):not([type="button"]):not([type="reset"]), select, textarea`: `inline-size: 100%; min-block-size: var(--control-height); padding: 0.5rem 0.75rem; background: var(--color-surface-sunken); color: var(--color-text); border: 1px solid var(--color-border-strong); border-radius: var(--radius-sm); transition: border-color fast;` hover `border-color: var(--color-text-muted)`; `:focus-visible` keeps the ring and sets `border-color: var(--color-accent)`; `:disabled { opacity: 0.6; cursor: not-allowed; }`; `[aria-invalid="true"], .invalid { border-color: var(--color-danger); }` (`.invalid` is Blazor's `InputBase` class).
- `textarea`: `min-block-size: 6rem; resize: vertical;`. `select`: native arrow (`appearance: auto`), no image.
- `::placeholder`: `color: var(--color-text-muted); opacity: 1;`
- `input[type="checkbox"], input[type="radio"]`: `accent-color: var(--color-accent); inline-size: 1.125rem; block-size: 1.125rem;`
- `.validation-message`: like `.sv-field__error` (below).
- `.validation-errors` (Blazor `ValidationSummary`): like `.sv-alert--danger`, `list-style: none`.

**`@keyframes`** (4.12): `sv-seal-wheel`, `sv-seal-bolts`, `sv-dial-tick`, `sv-reveal`.

**`sv-components`**

| Class | Spec |
|---|---|
| `.sv-icon` | `display: inline-block; inline-size: 1.25em; block-size: 1.25em; flex: none; vertical-align: -0.2em;` modifiers `--sm` (1em), `--lg` (1.5rem), `--xl` (3rem) |
| `.sv-plate` | plate label style (4.2) |
| `.sv-mono` | mono family and ligature settings |
| `.sv-btn` | `display: inline-flex; align-items: center; justify-content: center; gap: var(--space-sm); min-block-size: 2.25rem; padding-inline: var(--space-md); border: 1px solid var(--color-border-strong); border-radius: var(--radius-sm); background: var(--color-surface-raised); color: var(--color-text); font: 600 var(--text-sm)/1 var(--font-sans); font-stretch: 108%; letter-spacing: 0.01em; text-decoration: none; white-space: nowrap; cursor: pointer; transition: background-color fast, border-color fast, color fast;` hover (`:hover:not(:disabled):not([aria-disabled="true"])`) `border-color: var(--color-text-muted)`; `:active { transform: translateY(1px); }` (no lift or bounce: machined parts do not float); `:disabled, [aria-disabled="true"] { opacity: 0.55; cursor: not-allowed; }`; `.sv-btn .sv-icon { inline-size: 1.125em; block-size: 1.125em; }` |
| `.sv-btn--primary` | `background: var(--color-accent); border-color: var(--color-accent); color: var(--color-on-accent); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.25);` hover `background` and `border-color` `var(--color-accent-hover)` (polished brass) |
| `.sv-btn--secondary` | same as base (explicit name) |
| `.sv-btn--ghost` | `background: transparent; border-color: transparent;` hover `background: var(--color-surface-raised)` |
| `.sv-btn--danger` | `background: transparent; border-color: var(--color-danger); color: var(--color-danger);` hover `background: var(--color-danger-bg)` |
| `.sv-btn--danger-solid` | `background: var(--color-danger-solid); border-color: var(--color-danger-solid); color: var(--color-on-danger);` hover `-hover` token. Used only inside a confirm dialog footer. |
| `.sv-btn--icon` | `padding: 0; inline-size: 2.25rem;` (must carry `aria-label`, 7.1 scan) |
| `.sv-btn--sm` | `min-block-size: 1.875rem; padding-inline: var(--space-sm);` (with `--icon`: `inline-size: 1.875rem`) |
| `.sv-btn--block` | `inline-size: 100%;` |
| `.sv-field` | `display: grid; gap: var(--space-xs);` |
| `.sv-field__label` | `font-weight: 600; font-size: var(--text-sm);` |
| `.sv-field__required` | `color: var(--color-text-muted); font-weight: 400;` (the markup contains the text "(required)", never a lone asterisk) |
| `.sv-field__hint` | `font-size: var(--text-sm); color: var(--color-text-muted);` |
| `.sv-field__error`, `.validation-message` | `font-size: var(--text-sm); font-weight: 600; color: var(--color-danger);` and `::before { content: "Error: "; }` (a text cue, not colour only) |
| `.sv-input--secret` | mono family, ligature settings, `letter-spacing: 0.04em;` |
| `.sv-card` | `background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius-md); box-shadow: var(--shadow-raised);` |
| `.sv-card__header` | `display: flex; align-items: center; justify-content: space-between; gap: var(--space-md); padding: var(--space-md) var(--space-lg); border-bottom: 1px solid var(--color-border);` |
| `.sv-card__title` | `font-size: var(--text-lg); font-weight: 650; font-stretch: 108%;` |
| `.sv-card__body` | `padding: var(--space-lg);` |
| `.sv-card__footer` | `display: flex; justify-content: flex-end; gap: var(--space-sm); padding: var(--space-md) var(--space-lg); border-top: 1px solid var(--color-border);` |
| `.sv-card--plate` | `padding: var(--space-xl);` and four rivets: `background-image: radial-gradient(circle at 0.75rem 0.75rem, var(--color-rivet) 0 2px, transparent 2.5px), radial-gradient(circle at calc(100% - 0.75rem) 0.75rem, var(--color-rivet) 0 2px, transparent 2.5px), radial-gradient(circle at 0.75rem calc(100% - 0.75rem), var(--color-rivet) 0 2px, transparent 2.5px), radial-gradient(circle at calc(100% - 0.75rem) calc(100% - 0.75rem), var(--color-rivet) 0 2px, transparent 2.5px);` |
| `.sv-dl` | `display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: var(--space-sm) var(--space-lg); align-items: baseline;` `dt` plate style; `dd` `margin: 0; overflow-wrap: anywhere;` below 30rem one column |
| `.sv-table-wrap` | `overflow-x: auto; border: 1px solid var(--color-border); border-radius: var(--radius-md); background: var(--color-surface);` |
| `.sv-table` | `inline-size: 100%; border-collapse: separate; border-spacing: 0; font-size: var(--text-sm); font-variant-numeric: tabular-nums;` `th` plate style, `text-align: start; padding: var(--space-sm) var(--space-md); border-bottom: 1px solid var(--color-border-strong); background: var(--color-surface); white-space: nowrap;` `td` `padding: var(--space-sm) var(--space-md); border-bottom: 1px solid var(--color-border); vertical-align: middle;` last row no border; `tbody tr:hover td { background: var(--color-surface-raised); }` `.sv-table__actions { text-align: end; white-space: nowrap; }` (no stacked mobile layout: tables scroll horizontally) |
| `.sv-list` | `list-style: none; border: 1px solid var(--color-border); border-radius: var(--radius-md); background: var(--color-surface);` |
| `.sv-list__item` | `display: flex; align-items: center; gap: var(--space-md); padding: var(--space-sm) var(--space-md); border-bottom: 1px solid var(--color-border);` last child no border; hover `background: var(--color-surface-raised)` |
| `.sv-list__main` | `flex: 1; min-inline-size: 0; display: grid;` `.sv-list__title { font-weight: 600; }` with truncation; `.sv-list__meta { font-size: var(--text-sm); color: var(--color-text-muted); }`; `.sv-list__actions { display: flex; gap: var(--space-xs); }` |
| `.sv-tag` | `display: inline-flex; align-items: center; gap: 0.25rem; padding: 0.125rem 0.5rem; border: 1px solid var(--color-border-strong); border-radius: var(--radius-sm); background: var(--color-surface-sunken); color: var(--color-text-muted); font-size: var(--text-xs); font-weight: 600; letter-spacing: 0.04em;` |
| `.sv-tag--accent` | `color: var(--color-accent); border-color: var(--color-accent); background: var(--color-accent-soft);` |
| `.sv-status` | `display: inline-flex; align-items: center; gap: 0.375rem; padding: 0.125rem 0.5rem 0.125rem 0.375rem; border: 1px solid; border-radius: var(--radius-round); font-size: var(--text-xs); font-weight: 650; font-stretch: 112%; text-transform: uppercase; letter-spacing: 0.08em;` |
| `.sv-status--unknown` | `color: var(--color-text-muted); background: var(--color-surface-sunken); border-color: var(--color-border-strong);` |
| `.sv-status--active`, `--locked`, `--warning`, `--danger` | `color`, `background` and `border-color` from `--color-success*`, `--color-locked*`, `--color-warning*`, `--color-danger*` |
| `.sv-alert` | `display: grid; grid-template-columns: auto minmax(0, 1fr); gap: var(--space-sm) var(--space-md); padding: var(--space-md); border: 1px solid; border-inline-start-width: 4px; border-radius: var(--radius-md); color: var(--color-text);` `.sv-alert__title { font-weight: 650; }` |
| `.sv-alert--info` | `background: var(--color-surface-raised); border-color: var(--color-border-strong);` icon `color: var(--color-text-muted)` |
| `.sv-alert--success`, `--warning`, `--danger` | `background: var(--color-{kind}-bg); border-color: var(--color-{kind}-border); border-inline-start-color: var(--color-{kind});` and `.sv-alert__icon { color: var(--color-{kind}); }` (`success` for success) |
| `.sv-secret` | `display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: var(--space-sm) var(--space-md); padding: var(--space-sm) var(--space-md); background: var(--color-surface-sunken); border: 1px solid var(--color-border-strong); border-radius: var(--radius-sm);` below 30rem `grid-template-columns: minmax(0, 1fr) auto;` and `.sv-secret__state { grid-column: 1 / -1; }` |
| `.sv-secret__state` | `display: inline-flex; align-items: center; gap: 0.375rem; font-size: var(--text-xs); font-weight: 650; text-transform: uppercase; letter-spacing: 0.1em; color: var(--color-text-muted);` |
| `.sv-secret__value` | mono family and ligature settings; `font-size: var(--text-md); letter-spacing: 0.04em; white-space: pre-wrap; overflow-wrap: anywhere; color: var(--color-text);` (`pre-wrap` keeps leading, trailing and repeated spaces in a password visible) |
| `.sv-secret__mask` | `letter-spacing: 0.2em; color: var(--color-text-muted); user-select: none;` |
| `.sv-secret__actions` | `display: inline-flex; gap: var(--space-xs);` |
| `.sv-secret[data-state="revealed"]` | `border: 2px solid var(--color-accent); background: var(--color-accent-soft); padding: calc(var(--space-sm) - 1px) calc(var(--space-md) - 1px);` its `.sv-secret__state { color: var(--color-accent); }` and `.sv-secret__value { animation: sv-reveal var(--duration-fast) var(--ease-out); }` |
| `.sv-meter` | strength meter without inline widths (4.14): `display: inline-grid; grid-template-columns: repeat(4, 2rem); gap: 3px;` children `span { block-size: 0.375rem; border-radius: 2px; background: var(--color-border); }`; `[data-level="1"] span:nth-child(-n+1) { background: var(--color-danger); }`, `[data-level="2"] span:nth-child(-n+2) { background: var(--color-warning); }`, `[data-level="3"] span:nth-child(-n+3)` and `[data-level="4"] span:nth-child(-n+4) { background: var(--color-success); }`. The markup always has a text label next to it. |
| `.sv-dialog-backdrop` | `position: fixed; inset: 0; z-index: var(--z-dialog); display: grid; place-items: center; padding: var(--space-lg); background: var(--color-overlay);` (no `pointer-events: none`: it must block clicks to the page behind) |
| `.sv-dialog` | `inline-size: min(100%, 32rem); max-block-size: calc(100dvh - 2 * var(--space-lg)); overflow: auto; background: var(--color-surface-raised); border: 1px solid var(--color-border-strong); border-top: 2px solid var(--color-accent); border-radius: var(--radius-md); box-shadow: var(--shadow-dialog);` `:focus { outline: none; }` (it receives programmatic focus); `.sv-dialog__header { padding: var(--space-md) var(--space-lg) 0; }` `.sv-dialog__title { font-size: var(--text-lg); display: flex; gap: var(--space-sm); align-items: center; }` `.sv-dialog__body { padding: var(--space-md) var(--space-lg); }` `.sv-dialog__footer { display: flex; justify-content: flex-end; gap: var(--space-sm); padding: var(--space-md) var(--space-lg); border-top: 1px solid var(--color-border); }`; below 40rem the backdrop uses `place-items: end center; padding: 0;` and the dialog `inline-size: 100%; border-radius: var(--radius-md) var(--radius-md) 0 0; padding-bottom: env(safe-area-inset-bottom);` (bottom sheet) |
| `.sv-dialog--danger` | `border-top-color: var(--color-danger);` and the title icon `color: var(--color-danger)` |
| `.sv-tree`, `.sv-tree ul` | `list-style: none;` nested `ul { margin-inline-start: 0.875rem; padding-inline-start: var(--space-sm); border-inline-start: 1px solid var(--color-border); }` (the steel guide line) |
| `.sv-tree__row` | `display: flex; align-items: center; gap: var(--space-xs); min-block-size: 2rem; border-radius: var(--radius-sm);` hover `background: var(--color-surface-raised)` |
| `.sv-tree__toggle` | `display: inline-grid; place-items: center; inline-size: 1.5rem; block-size: 1.5rem; border: 0; border-radius: var(--radius-sm); background: none; color: var(--color-text-muted); cursor: pointer;` its `.sv-icon { transition: transform fast; }`; `[aria-expanded="true"] .sv-icon { transform: rotate(90deg); }` (with the `ChevronRight` icon) |
| `.sv-tree__spacer` | `inline-size: 1.5rem; flex: none;` (leaf nodes) |
| `.sv-tree__link` | `flex: 1; min-inline-size: 0; display: flex; align-items: center; gap: var(--space-sm); padding: var(--space-xs) var(--space-sm); color: var(--color-text); text-decoration: none;` label truncated; `[aria-current="page"], .active { font-weight: 650; background: var(--color-surface-raised); box-shadow: inset 3px 0 0 var(--color-accent); }` (bar and weight: not colour only) |
| `.sv-tree__actions` | `display: flex; gap: 2px;` inside `@media (hover: hover)` only: `opacity: 0` until `.sv-tree__row:hover` or `:focus-within` (always visible on touch devices) |
| `.sv-empty` | `display: grid; justify-items: center; gap: var(--space-md); padding: var(--space-2xl) var(--space-lg); text-align: center; border: 1px dashed var(--color-border-strong); border-radius: var(--radius-lg); background: var(--color-surface);` `.sv-empty__visual { display: grid; place-items: center; inline-size: 5rem; block-size: 5rem; border: 2px solid var(--color-border-strong); border-radius: 50%; color: var(--color-text-muted); }` (a deposit-box porthole) `.sv-empty__title { font-size: var(--text-lg); }` `.sv-empty__body { color: var(--color-text-muted); max-inline-size: 40ch; }` `.sv-empty__actions` cluster |
| `.sv-state` | `display: grid; justify-items: center; gap: var(--space-md); max-inline-size: 36rem; margin: var(--space-2xl) auto; text-align: center;` `.sv-state__visual { color: var(--color-text-muted); }` `.sv-state--danger .sv-state__visual { color: var(--color-danger); }` `.sv-state__body { color: var(--color-text-muted); }` `.sv-state__actions { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--space-sm); }` |
| `.sv-hero` | `display: grid; gap: var(--space-xl); align-items: center;` from 48rem `grid-template-columns: auto minmax(0, 1fr);` `.sv-hero__title` (4.2) `.sv-hero__tagline { font-size: var(--text-lg); color: var(--color-text-muted); max-inline-size: 40ch; }` `.sv-hero__actions { margin-top: var(--space-md); }` |
| `.sv-page-header` | `display: flex; flex-wrap: wrap; align-items: end; justify-content: space-between; gap: var(--space-md);` |
| `.sv-toolbar` | `display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-sm);` |
| `.sv-busy` | `display: inline-flex; align-items: center; gap: var(--space-sm); color: var(--color-text-muted); font-size: var(--text-sm);` `.sv-busy__dial { inline-size: 1.25rem; block-size: 1.25rem; }` `.sv-busy__ring { fill: none; stroke: var(--color-border-strong); stroke-width: 1.5; }` `.sv-busy__ticks line { stroke: var(--color-border-strong); stroke-width: 1.25; stroke-linecap: round; }` `.sv-busy__pointer { stroke: var(--color-accent); stroke-width: 2; stroke-linecap: round; transform-box: view-box; transform-origin: 12px 12px; animation: sv-dial-tick 1.2s steps(12, end) infinite; }` `.sv-busy__pointer circle { fill: var(--color-accent); stroke: none; }` |
| `.sv-lockscreen` | `position: fixed; inset: 0; z-index: var(--z-lock); display: grid; place-items: center; padding: max(var(--space-lg), env(safe-area-inset-top)) max(var(--space-lg), env(safe-area-inset-right)) max(var(--space-lg), env(safe-area-inset-bottom)) max(var(--space-lg), env(safe-area-inset-left)); background: var(--color-bg);` (opaque, never `--color-overlay`) `.sv-lockscreen__panel { display: grid; justify-items: center; gap: var(--space-md); max-inline-size: 28rem; text-align: center; }` |
| `.vault-door` and parts | see 4.9 |

**`sv-layout`** (shell, 4.10)

- `.sv-skip-link`: `position: absolute; inset-inline-start: var(--space-md); top: -10rem; z-index: var(--z-skip); padding: var(--space-sm) var(--space-md); border-radius: var(--radius-sm); background: var(--color-accent); color: var(--color-on-accent); font-weight: 700; text-decoration: none;` `:focus { top: calc(env(safe-area-inset-top) + var(--space-sm)); }`
- `.sv-frame`: `min-block-size: 100dvh; display: grid; grid-template-columns: minmax(0, 1fr); grid-template-rows: auto auto 1fr; grid-template-areas: "header" "nav" "main"; background: radial-gradient(120% 70% at 50% -10%, var(--color-bg-glow) 0%, transparent 60%), var(--color-bg);` from 60rem: `grid-template-columns: auto minmax(0, 1fr); grid-template-rows: auto 1fr; grid-template-areas: "header header" "nav main";` (an absent side nav collapses its `auto` track to zero, so the signed-out layout needs no extra class)
- `.sv-header`: `grid-area: header; position: sticky; top: 0; z-index: var(--z-header); display: flex; align-items: center; justify-content: space-between; gap: var(--space-md); min-block-size: calc(var(--header-height) + env(safe-area-inset-top)); padding: env(safe-area-inset-top) max(var(--space-md), env(safe-area-inset-right)) 0 max(var(--space-md), env(safe-area-inset-left)); background-color: var(--color-surface); background-image: repeating-linear-gradient(90deg, var(--color-grain) 0 1px, transparent 1px 4px); border-bottom: 1px solid var(--color-border); box-shadow: inset 0 2px 0 var(--color-accent);` (brushed steel with a brass rim) from 60rem horizontal padding `var(--space-lg)`
- `.sv-brand`: `display: inline-flex; align-items: center; gap: var(--space-sm); color: var(--color-text); text-decoration: none;` `.sv-brand__name` (4.2)
- `.sv-account`: `display: flex; align-items: center; justify-content: flex-end; gap: var(--space-sm); flex-wrap: wrap;` `.sv-account__who { display: inline-flex; align-items: center; gap: var(--space-xs); color: var(--color-text-muted); font-size: var(--text-sm); }` `.sv-account__form { display: contents; }` below 30rem `.sv-account__who { display: none; }`
- `.sv-sidenav`: `grid-area: nav; background: var(--color-surface); border-bottom: 1px solid var(--color-border); padding: var(--space-xs) max(var(--space-sm), env(safe-area-inset-right)) var(--space-xs) max(var(--space-sm), env(safe-area-inset-left)); overflow-x: auto;` from 60rem `inline-size: var(--sidenav-width); border-bottom: 0; border-inline-end: 1px solid var(--color-border); padding: var(--space-md) var(--space-sm); overflow-x: visible;`
- `.sv-nav`: `list-style: none; display: flex; gap: var(--space-xs);` from 60rem `display: grid; gap: 2px;` `.sv-nav__section` plate style, `padding: var(--space-md) var(--space-md) var(--space-xs);` (for later groups)
- `.sv-nav__link`: `display: flex; align-items: center; gap: var(--space-sm); min-block-size: 2.5rem; padding: var(--space-sm) var(--space-md); border-radius: var(--radius-sm); color: var(--color-text-muted); font-weight: 550; text-decoration: none; white-space: nowrap; transition: background-color fast, color fast;` hover `color: var(--color-text); background: var(--color-surface-raised);` current (`.active, [aria-current="page"]`) `color: var(--color-text); font-weight: 700; background: var(--color-surface-raised); box-shadow: inset 0 -3px 0 var(--color-accent);` and from 60rem `box-shadow: inset 3px 0 0 var(--color-accent);` (bar and weight, never colour alone)
- `.sv-main`: `grid-area: main; min-inline-size: 0; padding: var(--space-lg) max(var(--space-md), env(safe-area-inset-right)) max(var(--space-xl), env(safe-area-inset-bottom)) max(var(--space-md), env(safe-area-inset-left));` from 60rem `padding: var(--space-xl);` `:focus { outline: none; }`
- `.sv-content`: `max-inline-size: var(--content-max); margin-inline: auto; display: grid; gap: var(--space-xl);`
- `#blazor-error-ui`: `display: none; position: fixed; inset-inline: 0; bottom: 0; z-index: var(--z-error-ui); padding: var(--space-sm) 3.5rem var(--space-sm) var(--space-lg); padding-bottom: max(var(--space-sm), env(safe-area-inset-bottom)); background: var(--color-danger-bg); color: var(--color-text); border-top: 2px solid var(--color-danger); box-shadow: var(--shadow-dialog);` (Blazor sets `display: block`, V8) `.sv-icon { color: var(--color-danger); margin-inline-end: var(--space-xs); }` `.reload { margin-inline-start: var(--space-sm); }` `.dismiss { position: absolute; top: var(--space-xs); inset-inline-end: var(--space-md); }` (the dismiss button also has `sv-btn sv-btn--ghost sv-btn--icon sv-btn--sm`)

**`sv-utilities`**

- `.sv-visually-hidden`: the standard clip pattern (`position: absolute; inline-size: 1px; block-size: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; border: 0;`).
- `.sv-muted { color: var(--color-text-muted); }`, `.sv-stack { display: grid; gap: var(--space-md); }`, `.sv-stack--lg { gap: var(--space-xl); }`, `.sv-cluster { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-sm); }`, `.sv-cluster--end { justify-content: flex-end; }`, `.sv-truncate { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }`.
- `@media (pointer: coarse) { .sv-btn, .sv-nav__link, .sv-tree__toggle, .sv-tree__link, input, select { min-block-size: var(--tap-target); } .sv-btn--icon, .sv-tree__toggle { min-inline-size: var(--tap-target); } }`
- `@media (forced-colors: active) { .sv-btn, .sv-status, .sv-tag, .sv-secret, .sv-alert, .sv-dialog, .sv-card { border: 1px solid CanvasText; } .sv-nav__link.active, .sv-nav__link[aria-current="page"], .sv-tree__link[aria-current="page"] { outline: 2px solid Highlight; } :focus-visible { outline-color: Highlight; } .vault-door__wheel { stroke: CanvasText; } }`
- `@media print { .sv-header, .sv-sidenav, .sv-skip-link, #blazor-error-ui, .sv-secret__actions, .sv-dialog-backdrop, .sv-lockscreen { display: none !important; } .sv-secret[data-state="revealed"] .sv-secret__value { visibility: hidden; } body, .sv-frame { background: #FFFFFF; color: #000000; } }` (printing never shows a revealed secret)

### 4.7 Razor components

All parameters are `[Parameter]` properties. Ids are passed in and validated by `ComponentIds.EnsureValid` in `OnParametersSet`, so ids are stable and predictable for Page Objects.

**`Components/ComponentIds.cs`**: `public static partial class ComponentIds` with `[GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)] private static partial Regex ValidId();` and `public static string EnsureValid(string? value, string parameterName)`, which throws `ArgumentException` (message `"{parameterName} must be lowercase kebab-case."`, the value is not echoed) for null, whitespace or non-matching values, and returns the value. GUIDs in `D` format (lowercase) are valid inside ids.

| Component | Parameters | Markup (structure, classes and ids) |
|---|---|---|
| `Icons/Icon.razor` | `IconName Name`, `string? Label`, `string? CssClass` | `<svg class="sv-icon {CssClass}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" focusable="false">`. If `Label` is null or whitespace: `aria-hidden="true"` and no role. Otherwise `role="img"`, `aria-label="{Label}"` and a first child `<title>{Label}</title>`. Then a `@switch (Name)` that renders the inner elements from 4.8 exactly. No `width`, `height`, `style`, `href` or `<use>`. |
| `Brand/VaultDoor.razor` | `VaultDoorVariant Variant` (`Hero = 0`, `Mark`), `bool Sealing`, `string? Id` | the SVG in 4.9 with classes `vault-door vault-door--hero` or `vault-door vault-door--mark`, plus `vault-door--sealing` when `Sealing`. `aria-hidden="true" focusable="false"`; `id` only when `Id` is set (validated). |
| `Common/StatusBadge.razor` | `SecurityStatus Status` (`Unknown = 0`, `Active`, `Locked`, `Warning`, `Danger`), `string? Label`, `IconName? Icon`, `string? Id` | `<span id? class="sv-status sv-status--{status lowercase}"><Icon Name="{Icon ?? default}" /><span class="sv-status__label">{label}</span></span>`. Default icons: Unknown `Info`, Active `ShieldCheck`, Locked `Lock`, Warning `Warning`, Danger `Danger`. Default labels: "Unknown", "Active", "Locked", "Warning", "Danger". A null or whitespace `Label` falls back to the default, so text is never empty. |
| `Common/Alert.razor` | `string Id` (required), `AlertKind Kind` (`Info = 0`, `Success`, `Warning`, `Danger`), `string? Title`, `RenderFragment? ChildContent` | `<div id="{Id}" class="sv-alert sv-alert--{kind lowercase}" role="{alert for Warning and Danger, status otherwise}"><Icon Name="{Info, Success, Warning, Danger}" CssClass="sv-alert__icon" /><div class="sv-alert__body"><p class="sv-alert__title"><span class="sv-visually-hidden">{Information, Success, Warning, Error}: </span>{Title}</p>{ChildContent}</div></div>`. When `Title` is null the `<p>` holds only the hidden prefix. |
| `Common/EmptyState.razor` | `string Id` (required), `string Title` (required), `IconName Icon = IconName.Vault`, `RenderFragment? ChildContent`, `RenderFragment? Actions` | `<section id="{Id}" class="sv-empty" aria-labelledby="{Id}-title"><div class="sv-empty__visual"><Icon Name="{Icon}" CssClass="sv-icon--xl" /></div><h2 id="{Id}-title" class="sv-empty__title">{Title}</h2><div class="sv-empty__body">{ChildContent}</div>{Actions in <div class="sv-empty__actions">, only if set}</section>` |
| `Common/DialogShell.razor` | `string IdPrefix` (required), `string Title` (required), `bool Open`, `bool Danger`, `RenderFragment? ChildContent`, `RenderFragment? Footer`, `EventCallback OnCancel` | Renders nothing when `!Open`. When open: `<div id="{IdPrefix}-backdrop" class="sv-dialog-backdrop"><section id="{IdPrefix}-dialog" class="sv-dialog {sv-dialog--danger}" role="dialog" aria-modal="true" aria-labelledby="{IdPrefix}-title" tabindex="-1" @ref="_dialog" @onkeydown="HandleKeyDownAsync"><header class="sv-dialog__header"><h2 id="{IdPrefix}-title" class="sv-dialog__title">{Danger: <Icon Name="IconName.Danger" />}{Title}</h2></header><div class="sv-dialog__body">{ChildContent}</div>{Footer in <footer class="sv-dialog__footer">, only if set}</section></div>`. `OnAfterRenderAsync`: when `Open` changed from false (or first render) to true, `await _dialog.FocusAsync()`. Focus goes to the dialog, **never** to a button inside it. `HandleKeyDownAsync`: `Key == "Escape"` invokes `OnCancel`. Clicking the backdrop does nothing (D6). |
| `Common/SecretFrame.razor` | `string IdPrefix` (required), `bool Revealed` (default false: hidden), `RenderFragment? ChildContent` (the value), `RenderFragment? Actions` | `public const string MaskText = "••••••••";` (eight U+2022, fixed regardless of the value's length). `<div id="{IdPrefix}-frame" class="sv-secret" data-state="{revealed or hidden}"><span id="{IdPrefix}-state" class="sv-secret__state"><Icon Name="{Eye or Lock}" /><span>{Visible or Hidden}</span></span><span id="{IdPrefix}" class="sv-secret__value notranslate" translate="no">{Revealed ? ChildContent : <span class="sv-secret__mask" aria-hidden="true">{MaskText}</span><span class="sv-visually-hidden">Hidden value</span>}</span>{Actions in <span class="sv-secret__actions">, only if set}</div>`. `ChildContent` is **not rendered at all** while hidden, even if supplied. |
| `Common/BusyIndicator.razor` | `string Id` (required), `string Label = "Working"` | `<span id="{Id}" class="sv-busy" role="status"><svg class="sv-busy__dial" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><circle class="sv-busy__ring" cx="12" cy="12" r="10" /><g class="sv-busy__ticks">{for i in 0..11: <line x1="12" y1="2.75" x2="12" y2="4.25" transform="rotate({i * 30} 12 12)" />}</g><g class="sv-busy__pointer"><line x1="12" y1="12" x2="12" y2="5.5" /><circle cx="12" cy="12" r="1.75" /></g></svg><span class="sv-busy__label">{Label}</span></span>` |

Enum files (`IconName.cs`, `VaultDoorVariant.cs`, `SecurityStatus.cs`, `AlertKind.cs`) are `public enum` with the AGPL header, members in the order given, and no explicit values except where `= 0` is stated.

SVG `transform`, `fill`, `stroke` and similar are **presentation attributes, not inline styles**, so the CSP allows them. A `style` attribute or a `<style>` element would be blocked (D8, 4.14).

### 4.8 Icons

`IconName` members, in this order, and their inner elements copied verbatim from `lucide-static` 1.66.0 `icons/{file}` (V6). Copy only the child elements, never the `<!-- @license -->` comment or the outer `<svg>`.

| `IconName` | Lucide file | Inner elements |
|---|---|---|
| `Vault` | `vault.svg` | `<rect width="18" height="18" x="3" y="3" rx="2" /> <circle cx="7.5" cy="7.5" r=".5" fill="currentColor" /> <path d="m7.9 7.9 2.7 2.7" /> <circle cx="16.5" cy="7.5" r=".5" fill="currentColor" /> <path d="m13.4 10.6 2.7-2.7" /> <circle cx="7.5" cy="16.5" r=".5" fill="currentColor" /> <path d="m7.9 16.1 2.7-2.7" /> <circle cx="16.5" cy="16.5" r=".5" fill="currentColor" /> <path d="m13.4 13.4 2.7 2.7" /> <circle cx="12" cy="12" r="2" />` |
| `Lock` | `lock.svg` | `<rect width="18" height="11" x="3" y="11" rx="2" ry="2" /> <path d="M7 11V7a5 5 0 0 1 10 0v4" />` |
| `LockOpen` | `lock-open.svg` | `<rect width="18" height="11" x="3" y="11" rx="2" ry="2" /> <path d="M7 11V7a5 5 0 0 1 9.9-1" />` |
| `Eye` | `eye.svg` | `<path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0" /> <circle cx="12" cy="12" r="3" />` |
| `EyeOff` | `eye-off.svg` | `<path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49" /> <path d="M14.084 14.158a3 3 0 0 1-4.242-4.242" /> <path d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143" /> <path d="m2 2 20 20" />` |
| `Copy` | `copy.svg` | `<rect width="14" height="14" x="8" y="8" rx="2" ry="2" /> <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />` |
| `Key` | `key-round.svg` | `<path d="M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z" /> <circle cx="16.5" cy="7.5" r=".5" fill="currentColor" />` |
| `ShieldCheck` | `shield-check.svg` | `<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z" /> <path d="m9 12 2 2 4-4" />` |
| `ShieldAlert` | `shield-alert.svg` | `<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z" /> <path d="M12 8v4" /> <path d="M12 16h.01" />` |
| `Folder` | `folder.svg` | `<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z" />` |
| `FolderOpen` | `folder-open.svg` | `<path d="m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2" />` |
| `FolderMove` | `folder-input.svg` | `<path d="M2 9V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H20a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-1" /> <path d="M2 13h10" /> <path d="m9 16 3-3-3-3" />` |
| `Plus` | `plus.svg` | `<path d="M5 12h14" /> <path d="M12 5v14" />` |
| `Search` | `search.svg` | `<path d="m21 21-4.34-4.34" /> <circle cx="11" cy="11" r="8" />` |
| `Trash` | `trash-2.svg` | `<path d="M10 11v6" /> <path d="M14 11v6" /> <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" /> <path d="M3 6h18" /> <path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />` |
| `Restore` | `rotate-ccw.svg` | `<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" /> <path d="M3 3v5h5" />` |
| `History` | `history.svg` | `<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" /> <path d="M3 3v5h5" /> <path d="M12 7v5l4 2" />` |
| `Edit` | `pencil.svg` | `<path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z" /> <path d="m15 5 4 4" />` |
| `User` | `user-round.svg` | `<circle cx="12" cy="8" r="5" /> <path d="M20 21a8 8 0 0 0-16 0" />` |
| `SignOut` | `log-out.svg` | `<path d="m16 17 5-5-5-5" /> <path d="M21 12H9" /> <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />` |
| `Warning` | `triangle-alert.svg` | `<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" /> <path d="M12 9v4" /> <path d="M12 17h.01" />` |
| `Danger` | `octagon-x.svg` | `<path d="m15 9-6 6" /> <path d="M2.586 16.726A2 2 0 0 1 2 15.312V8.688a2 2 0 0 1 .586-1.414l4.688-4.688A2 2 0 0 1 8.688 2h6.624a2 2 0 0 1 1.414.586l4.688 4.688A2 2 0 0 1 22 8.688v6.624a2 2 0 0 1-.586 1.414l-4.688 4.688a2 2 0 0 1-1.414.586H8.688a2 2 0 0 1-1.414-.586z" /> <path d="m9 9 6 6" />` |
| `Info` | `info.svg` | `<circle cx="12" cy="12" r="10" /> <path d="M12 16v-4" /> <path d="M12 8h.01" />` |
| `Check` | `check.svg` | `<path d="M20 6 9 17l-5-5" />` |
| `Success` | `circle-check.svg` | `<circle cx="12" cy="12" r="10" /> <path d="m16 9-5.5 5.5L8 12" />` |
| `ChevronRight` | `chevron-right.svg` | `<path d="m9 18 6-6-6-6" />` |
| `ChevronDown` | `chevron-down.svg` | `<path d="m6 9 6 6 6-6" />` |
| `Close` | `x.svg` | `<path d="M18 6 6 18" /> <path d="m6 6 12 12" />` |
| `Generate` | `dices.svg` | `<rect width="12" height="12" x="2" y="10" rx="2" ry="2" /> <path d="m17.92 14 3.5-3.5a2.24 2.24 0 0 0 0-3l-5-4.92a2.24 2.24 0 0 0-3 0L10 6" /> <path d="M6 18h.01" /> <path d="M10 14h.01" /> <path d="M15 6h.01" /> <path d="M18 9h.01" />` |
| `Link` | `link.svg` | `<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" /> <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" />` |
| `ScheduledLock` | `clock-alert.svg` | `<path d="M12 6v6l4 2" /> <path d="M20 12v5" /> <path d="M20 21h.01" /> <path d="M21.25 8.2A10 10 0 1 0 16 21.16" />` |

### 4.9 Vault door and favicon

**`VaultDoor.razor` SVG** (exact, inside the component; `{classes}` and `{id}` per 4.7):

```xml
<svg class="{classes}" viewBox="0 0 120 120" aria-hidden="true" focusable="false">
  <rect class="vault-door__hinge" x="1" y="38" width="8" height="14" rx="2" />
  <rect class="vault-door__hinge" x="1" y="68" width="8" height="14" rx="2" />
  <circle class="vault-door__rim" cx="60" cy="60" r="56" />
  <g class="vault-door__bolts">
    <rect x="57" y="8" width="6" height="10" rx="1.5" transform="rotate(0 60 60)" />
    <rect x="57" y="8" width="6" height="10" rx="1.5" transform="rotate(45 60 60)" />
    <rect x="57" y="8" width="6" height="10" rx="1.5" transform="rotate(90 60 60)" />
    <rect x="57" y="8" width="6" height="10" rx="1.5" transform="rotate(135 60 60)" />
    <rect x="57" y="8" width="6" height="10" rx="1.5" transform="rotate(180 60 60)" />
    <rect x="57" y="8" width="6" height="10" rx="1.5" transform="rotate(225 60 60)" />
    <rect x="57" y="8" width="6" height="10" rx="1.5" transform="rotate(270 60 60)" />
    <rect x="57" y="8" width="6" height="10" rx="1.5" transform="rotate(315 60 60)" />
  </g>
  <circle class="vault-door__face" cx="60" cy="60" r="44" />
  <circle class="vault-door__face-ring" cx="60" cy="60" r="36" />
  <g class="vault-door__wheel">
    <line x1="60" y1="60" x2="60" y2="34" />
    <line x1="60" y1="60" x2="82.52" y2="73" />
    <line x1="60" y1="60" x2="37.48" y2="73" />
    <circle cx="60" cy="34" r="3.5" />
    <circle cx="82.52" cy="73" r="3.5" />
    <circle cx="37.48" cy="73" r="3.5" />
    <circle class="vault-door__hub" cx="60" cy="60" r="9" />
  </g>
</svg>
```

The bolts sit between radius 42 and 52, so the face (radius 44) hides their inner end. Scaling the bolt group to 0.86 hides them behind the face (retracted); scale 1 shows them engaged.

Styles (in `sv-components`):

- `.vault-door { display: block; block-size: auto; overflow: visible; }` `.vault-door--hero { inline-size: clamp(8rem, 24vw, 13rem); }` `.vault-door--mark { inline-size: 1.75rem; block-size: 1.75rem; }`
- `.vault-door__hinge { fill: var(--color-border-strong); }` `.vault-door__rim { fill: var(--color-surface); stroke: var(--color-border-strong); stroke-width: 3; }` `.vault-door__bolts { transform-box: view-box; transform-origin: 60px 60px; }` `.vault-door__bolts rect { fill: var(--color-text-muted); }` `.vault-door__face { fill: var(--color-surface-raised); stroke: var(--color-border); stroke-width: 1.5; }` `.vault-door__face-ring { fill: none; stroke: var(--color-border); stroke-width: 1; stroke-dasharray: 2 4; }` (dial graduations) `.vault-door__wheel { fill: none; stroke: var(--color-accent); stroke-width: 4; stroke-linecap: round; transform-box: view-box; transform-origin: 60px 60px; }` `.vault-door__wheel circle { fill: var(--color-accent); stroke: none; }` `.vault-door__wheel .vault-door__hub { fill: var(--color-surface-raised); stroke: var(--color-accent); stroke-width: 3; }`
- Mark: `.vault-door--mark .vault-door__hinge, .vault-door--mark .vault-door__bolts, .vault-door--mark .vault-door__face-ring { display: none; }` `.vault-door--mark .vault-door__rim { stroke: var(--color-accent); stroke-width: 8; }` `.vault-door--mark .vault-door__wheel { stroke-width: 9; }`
- Sealing: `.vault-door--sealing .vault-door__wheel { animation: sv-seal-wheel var(--duration-seal) var(--ease-lock) both; }` `.vault-door--sealing .vault-door__bolts { animation: sv-seal-bolts 260ms var(--ease-out) 520ms both; }`

**`wwwroot/brand/favicon.svg`** (exact, one line, fixed colours so it works on light and dark browser chrome, no `<style>`, no script, no links):

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><circle cx="16" cy="16" r="15" fill="#0A1017"/><circle cx="16" cy="16" r="12" fill="none" stroke="#D9AE55" stroke-width="2"/><g stroke="#D9AE55" stroke-width="2.5" stroke-linecap="round"><line x1="16" y1="16" x2="16" y2="8.5"/><line x1="16" y1="16" x2="22.5" y2="19.75"/><line x1="16" y1="16" x2="9.5" y2="19.75"/></g><circle cx="16" cy="16" r="3" fill="#D9AE55"/></svg>
```

### 4.10 Layout shell

`Layout/MainLayout.razor` (after T0):

```
<SkipLink />
<div id="app-frame" class="sv-frame">
  <AppHeader />
  <SideNav />
  <main id="main-content" class="sv-main" tabindex="-1">
    <div id="app-shell" class="sv-content">@Body</div>
  </main>
</div>
<div id="blazor-error-ui" role="alert" data-nosnippet>
  <Icon Name="IconName.Danger" />
  <span>An unhandled error has occurred.</span>
  <a id="blazor-error-reload" href="." class="reload">Reload</a>
  <button id="blazor-error-dismiss" type="button" class="dismiss sv-btn sv-btn--ghost sv-btn--icon sv-btn--sm" aria-label="Dismiss"><Icon Name="IconName.Close" /></button>
</div>
```

- `Layout/SkipLink.razor`: injects `NavigationManager`, implements `IDisposable`. Renders `<a id="skip-to-content" class="sv-skip-link" href="{Href}">Skip to content</a>`, where `Href` is `ToBaseRelativePath(Uri)` with any `#...` removed, followed by `#main-content`. Subscribes to `LocationChanged` (re-renders with `InvokeAsync(StateHasChanged)`) and unsubscribes in `Dispose`. Reason: with `<base href="/">`, a bare `#main-content` resolves to the home page on every other route (D14).
- `Layout/AppHeader.razor`: `<header id="app-header" class="sv-header"><a id="brand-home-link" class="sv-brand" href=""><VaultDoor Variant="VaultDoorVariant.Mark" /><span class="sv-brand__name">SharpVault</span></a><div class="sv-header__actions"><AccountMenu /></div></header>`
- `Layout/SideNav.razor`: `<AuthorizeView><Authorized><nav id="side-nav" class="sv-sidenav" aria-label="Main"><ul class="sv-nav"><li><NavLink id="nav-home" class="sv-nav__link" href="" Match="NavLinkMatch.All"><Icon Name="IconName.Vault" /><span>Home</span></NavLink></li></ul></nav></Authorized></AuthorizeView>`. Signed-out users get no side nav. Later features add their `<li>` items here (`nav-vault`, `nav-recycle-bin`, and so on, D10).
- `Layout/AccountMenu.razor` (restyle of entra-sign-in's component; ids and visible texts unchanged): `<nav id="account-menu" class="sv-account" aria-label="Account">` with, when authorized, `<span class="sv-account__who"><Icon Name="IconName.User" /><span id="account-name" class="sv-account__name">{name}</span></span>`, `<a id="account-link" class="sv-btn sv-btn--ghost sv-btn--sm" href="account">Account</a>` and the existing `<form id="sign-out-form" class="sv-account__form" ...>` whose `<button id="sign-out-button" class="sv-btn sv-btn--secondary sv-btn--sm" type="submit"><Icon Name="IconName.SignOut" /><span>Sign out</span></button>`; when not authorized `<a id="sign-in-link" class="sv-btn sv-btn--primary sv-btn--sm" href="{SignIn}"><Icon Name="IconName.Key" /><span>Sign in</span></a>`.
- Delete `Layout/MainLayout.razor.css` and `Layout/AccountMenu.razor.css`. All shell styles are global (D4).

`SharpVault.Web/Components/App.razor` head (after `<base href="/" />`, keeping `<ResourcePreloader />`, both stylesheets and `<HeadOutlet />`):

- `<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover" />` (replaces the existing viewport meta; `viewport-fit=cover` makes `env(safe-area-inset-*)` non-zero on notched devices)
- `<meta name="color-scheme" content="dark light" />` (the browser paints the right background before CSS loads, so there is no white flash)
- `<meta name="theme-color" content="#0A1017" media="(prefers-color-scheme: dark)" />` and `<meta name="theme-color" content="#ECEFF3" media="(prefers-color-scheme: light)" />`
- `<link rel="icon" type="image/svg+xml" href="@Assets["_content/SharpVault.UI.Shared/brand/favicon.svg"]" />`

### 4.11 Pages

Ids and visible texts stay exactly as they are (entra-sign-in tests match `id="x"[^>]*>Text<`, so the element that carries an id keeps its text as direct content; icons go in sibling elements).

| Page | Theme |
|---|---|
| `Home` | `<section id="home" class="sv-hero sv-card sv-card--plate">` with `<VaultDoor />` (static hero), then `<div class="sv-hero__text"><p class="sv-plate">Organisation vault</p><h1 id="home-heading" class="sv-hero__title">SharpVault</h1><p id="home-tagline" class="sv-hero__tagline">...</p><div class="sv-hero__actions">` with the existing `AuthorizeView`: authorized `<p id="home-greeting">Signed in as {name}</p>`; not authorized `<a id="home-sign-in" class="sv-btn sv-btn--primary" href="{SignIn}"><Icon Name="IconName.Key" /><span>Sign in with your work account</span></a>` |
| `NotFound` | `<section class="sv-state"><div class="sv-state__visual"><Icon Name="IconName.Vault" CssClass="sv-icon--xl" /></div><p class="sv-plate">404</p><h1 id="not-found-heading">Not found</h1><p id="not-found-message" class="sv-state__body">...</p><div class="sv-state__actions"><a id="not-found-home-link" class="sv-btn" href="">Home</a></div></section>` |
| `AccessDenied` | `sv-state sv-state--danger`, visual `ShieldAlert` xl, existing h1 and message, `access-denied-home-link` as `sv-btn` |
| `SignedOut` | `sv-state`, visual `<VaultDoor Sealing="true" />` (the one place the door animates), existing h1, `signed-out-sign-in-link` as `sv-btn sv-btn--primary` with `Key` icon in a sibling span |
| `SignInError` | `sv-state sv-state--danger`, visual `Danger` xl, existing h1 and message, `sign-in-error-retry-link` as `sv-btn sv-btn--primary` |
| `Account` | `<section id="account-section" class="sv-card sv-card--plate sv-stack">` with `<p class="sv-plate">Identity plate</p>`, the h1, `<dl class="sv-dl">` (dt texts unchanged), `account-user-id` gets `class="sv-mono"` (shown like a serial number), the admin badge becomes `<span id="account-admin-badge" class="sv-tag sv-tag--accent">Administrator</span>` preceded by a sibling `<Icon Name="IconName.ShieldCheck" />` inside a `sv-cluster` span. `account-error` becomes `<Alert Id="account-error" Kind="AlertKind.Danger">` with the existing text as content. Check: `Alert` puts the id on the outer div, so `id="account-error"` still matches. |
| Web `Error` | `sv-state sv-state--danger`, visual `Danger` xl, existing h1 and message, request id inside `<code class="sv-mono">` (unchanged text) |
| Web `Routes` `NotAuthorized` | `<section class="sv-state"><div class="sv-state__visual"><Icon Name="IconName.Lock" CssClass="sv-icon--xl" /></div><p id="not-authorized-message">You need to sign in to see this page.</p></section>` |
| Web `ReconnectModal` | `.razor.css` rewritten with tokens: modal `background: var(--color-surface-raised); color: var(--color-text); border: 1px solid var(--color-border-strong); border-top: 2px solid var(--color-accent); border-radius: var(--radius-md); box-shadow: var(--shadow-dialog);` backdrop `var(--color-overlay)`; rejoining rings `border-color: var(--color-accent)`; buttons get `sv-btn sv-btn--primary` in the markup (the isolated `display` rules still win, D4); remove the hard-coded `white`, `#6b9ed2`, `#3b6ea2`, `#0087ff` and `rgba(...)` values; remove the stray CSS nesting block and write the `[open]` rule as a normal selector. The JS file is unchanged. |

### 4.12 Motion

| Keyframes | Definition | Used by | Meaning |
|---|---|---|---|
| `sv-seal-wheel` | `from { transform: rotate(-120deg); } to { transform: rotate(0deg); }` | `.vault-door--sealing .vault-door__wheel`, 700 ms, `--ease-lock`, once | the handle turns: the vault is being closed |
| `sv-seal-bolts` | `from { transform: scale(0.86); } to { transform: scale(1); }` | `.vault-door--sealing .vault-door__bolts`, 260 ms after 520 ms, once | the bolts engage: sealed |
| `sv-dial-tick` | `to { transform: rotate(360deg); }` | `.sv-busy__pointer`, 1.2 s `steps(12, end)` infinite | a combination dial ticking while work happens |
| `sv-reveal` | `from { opacity: 0; } to { opacity: 1; }` | revealed `.sv-secret__value`, 120 ms, once | the value appears deliberately, not as a flash |

Transitions are colour and border only (120 ms), plus the tree chevron rotation. No hover lifts, parallax or page transitions. With `prefers-reduced-motion: reduce` every animation and transition completes instantly (4.6 reset), so the door shows its final sealed state and the busy dial shows a static pointer with its text label.

### 4.13 MAUI readiness

- The same `app.css`, fonts and favicon come from `_content/SharpVault.UI.Shared/` in BlazorWebView. Font `url()`s are relative to the stylesheet, so they resolve the same way in the WebView.
- `feature/maui-app` must: link `_content/SharpVault.UI.Shared/css/app.css` in `wwwroot/index.html`, use `<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover" />` and `<meta name="color-scheme" content="dark light" />`, and set the iOS and Mac Catalyst minimum to 15.4 or later (cascade layers, `:focus-visible`, `dvh`), D4. On Android, the WebView follows `prefers-color-scheme` from API 29.
- Safe areas: header top and sides, main sides and bottom, side-nav strip sides, dialog bottom sheet, lock screen and error bar all use `env(safe-area-inset-*)`.
- Touch: `pointer: coarse` raises every control to 44 px; hover-only affordances (tree row actions) are visible on touch devices; `-webkit-tap-highlight-color: transparent` with a visible `:active` state.
- No JS and no host-specific CSS. `Icon` and `VaultDoor` are inline SVG and need no assets.

### 4.14 CSP compatibility rules (for this work and every later UI feature)

- No `style="..."` attributes and no `<style>` elements anywhere (the web CSP has `style-src 'self'` without `'unsafe-inline'`; Blazor sets attributes with `setAttribute`, which the CSP blocks for `style`). Dynamic visuals use classes or `data-*` attributes with a fixed set of CSS rules, for example `.sv-meter[data-level="3"]`, never a computed width.
- No `<script>` without `src`, no `on*=` attributes, no `javascript:` URLs.
- No external origins in `src`, `href`, `action`, `formaction`, `poster` or CSS `url()`. No `<link rel="preconnect|dns-prefetch|prefetch|preload">`.
- SVG presentation attributes (`fill`, `stroke`, `transform`) are allowed.
- The CSP itself does not change (D8).

### 4.15 Contract for later features

`docs/THEME.md` (T10) repeats this table. Later plans reference it.

| Need | Use | Rule |
|---|---|---|
| Showing a secret value, history value or generated password | `SecretFrame` (`IdPrefix` = the existing id, for example `secret-value`, so the value element keeps that id) | Pass the value only when revealed. Never mask with CSS. The mask is always `SecretFrame.MaskText`. |
| An input that holds a secret value or secret notes | `class="sv-input--secret"` (or `type="password"`) | Must also have `spellcheck="false"`, `autocapitalize="off"`, `autocorrect="off"` and an explicit `autocomplete` (`new-password`, `current-password` or `off`). The 7.1 scan enforces it. |
| Modal dialogs and confirms | `DialogShell` with the confirm and cancel buttons in `Footer`, Cancel first in DOM order, confirm `sv-btn--primary` or (destructive) `sv-btn--danger-solid` with `Danger="true"` | Do not autofocus the confirm button. |
| User or item state (locked, active, scheduled lock) | `StatusBadge` (`Warning` with `Icon="IconName.ScheduledLock"` and `Label="Lock scheduled"` for scheduled locks) | Always text plus icon. |
| Messages | `Alert` | Danger and Warning are `role="alert"`. |
| Empty lists | `EmptyState` | |
| Loading | `BusyIndicator` | |
| Navigation entries | `<li>` items in `SideNav` with `NavLink`, `sv-nav__link`, an `Icon` and a `<span>` label, ids `nav-{name}` | Admin entries inside an `AuthorizeView` with the admin role (the API remains the boundary). |
| Folder tree | `ul.sv-tree > li > div.sv-tree__row` with `button.sv-tree__toggle[aria-expanded]` (or `span.sv-tree__spacer`), `a.sv-tree__link[aria-current]` with a `Folder` or `FolderOpen` icon, and `.sv-tree__actions`; children in a nested `ul` | |
| Lists of secrets or bin items | `ul.sv-list > li.sv-list__item` with `.sv-list__main`, `.sv-list__title`, `.sv-list__meta`, `.sv-list__actions` | |
| Tags | `span.sv-tag` | |
| Password strength | `span.sv-meter[data-level="0..4"]` with four `span` children, plus a visible text label | |
| Idle auto-lock (ADM-4) | `div.sv-lockscreen > div.sv-lockscreen__panel` with `<VaultDoor Sealing="true" />`, an h1 "Vault locked", text and an unlock button | The vault content behind it must be removed from the DOM, not just covered. The background is opaque. |
| Icon-only buttons | `sv-btn sv-btn--icon` | Must have `aria-label` (7.1 scan). |
| Component-specific styles | a `.razor.css` file (unlayered, overrides the theme) using tokens only | No new colour literals. |

## 5. Tasks

Ownership: `implementer` T1 to T6, `test-writer` T7 to T9, `docs-writer` T10. After each implementer task: `dotnet build SharpVault.slnx` reports `0 Warning(s)` and `0 Error(s)`, and `dotnet test SharpVault.slnx --filter "Category!=Performance"` stays green.

```
T1 (assets, packages)   ∥  T2 (app.css: fonts, tokens, reset, base, keyframes)  ∥  T4 (components)
                              └─ T3 (app.css: components, layout, utilities)
T0 (orchestrator: merge main with entra-sign-in)
 ├─ T5 (layout shell and pages; after T0 and T4)  ∥  T6 (Web host; after T0 and T1)
 ├─ T7 unit, bUnit, scan and contrast tests (after T1 to T5; the T1 to T4 parts may start before T0)
 ├─ T8 web integration tests (after T5, T6)
 ├─ T9 performance tests (after T6)
 └─ T10 docs (after all)
```

T2 and T3 edit the same file, so T3 waits for T2. T4 writes markup against the class names fixed in 4.6 and 4.7, so it does not wait for T2 or T3.

---

### T0. Merge prerequisite (orchestrator, not an implementer)

Wait until `feature/entra-sign-in` is merged into `main`, then merge `main` into `feature/app-theme`. Expect conflicts only if T1 to T4 already landed: `Directory.Packages.props` (keep both sides), `src/SharpVault.UI.Shared/_Imports.razor` (keep every `@using` once; a duplicate `@using` is CS0105, an error here) and `tests/SharpVault.UnitTests/SharpVault.UnitTests.csproj`.

**Acceptance**: build clean and all non-performance tests pass before T5 and T6 start.

### T1. Assets, notices and packages (first; parallel with T2 and T4)

1. Download into the implementer scratchpad (not the repository):
   - `https://registry.npmjs.org/@fontsource-variable/archivo/-/archivo-5.3.0.tgz`
   - `https://registry.npmjs.org/@fontsource-variable/jetbrains-mono/-/jetbrains-mono-5.3.0.tgz`
   - `https://registry.npmjs.org/lucide-static/-/lucide-static-1.66.0.tgz` (only for its `LICENSE` and to double-check 4.8)
   Check each tarball's integrity with `openssl dgst -sha512 -binary {file} | base64 -w0` against V4 and V6. Extract each into its own new empty directory.
2. Copy into `src/SharpVault.UI.Shared/wwwroot/fonts/` exactly the four woff2 files in 4.2 from `package/files/`, and the two package `LICENSE` files as `OFL-Archivo.txt` and `OFL-JetBrainsMono.txt`. Check every size and SHA-256 against 4.2 with `sha256sum` and `wc -c`. **If any value differs, stop and report to the planner.**
3. Create `src/SharpVault.UI.Shared/wwwroot/brand/favicon.svg` with the exact content in 4.9 and a trailing newline.
4. `.gitattributes`: add the line `src/SharpVault.UI.Shared/wwwroot/fonts/*.txt -text`, so the licence files keep their exact bytes and hashes on every platform. `woff2` and `svg` are already `binary`, so nothing else changes.
5. Create `THIRD-PARTY-NOTICES.md` at the repository root (or, if `feature/vault-and-folders` merged first, append sections to it):
   - "Archivo": "Copyright 2020 The Archivo Project Authors (https://github.com/Omnibus-Type/Archivo). SIL Open Font License 1.1. Files: `src/SharpVault.UI.Shared/wwwroot/fonts/archivo-*.woff2`, licence in `OFL-Archivo.txt`. Obtained unmodified from the npm package `@fontsource-variable/archivo` 5.3.0."
   - "JetBrains Mono": same pattern with "Copyright 2020 The JetBrains Mono Project Authors (https://github.com/JetBrains/JetBrainsMono)".
   - "Lucide": "Icon path data from `lucide-static` 1.66.0 (https://lucide.dev), embedded in `src/SharpVault.UI.Shared/Components/Icons/Icon.razor`." followed by the **full text** of the package `LICENSE` (ISC, and the MIT notice for icons derived from Feather) in a fenced block.
6. `Directory.Packages.props`: add `<PackageVersion Include="bunit" Version="2.13.3" />` in alphabetical order (skip if it is already there from the vault branch).
7. `tests/SharpVault.UnitTests/SharpVault.UnitTests.csproj`: add `<PackageReference Include="bunit" />` (skip if present).

**Acceptance**: build clean; `dotnet list SharpVault.slnx package --vulnerable --include-transitive` reports nothing; `ls src/SharpVault.UI.Shared/wwwroot/fonts` shows exactly the six files; the hashes match.

### T2. `app.css` part 1: fonts, tokens, reset, base, keyframes (parallel with T1 and T4)

Replace `src/SharpVault.UI.Shared/wwwroot/css/app.css` with items 1 to 7 of 4.5: the `@layer` statement, the four `@font-face` rules (4.2), the dark `:root` tokens and the light media block (4.3, every value exactly as listed), `@layer sv-reset` and `@layer sv-base` (4.6), and the four `@keyframes` (4.12). Remove `--color-error-bg`, `--color-error-text` and `--color-shadow`. Move the `#blazor-error-ui` rules out of this part (T3 writes them in `sv-layout`).

**Acceptance**: no comments in the file; the first top-level rule with selector `:root` holds every token; the light block holds only `color-scheme`, `--color-*` and `--shadow-*`; every `--color-*` value is `#RRGGBB` except the `-grain` and `-overlay` tokens; the only `url(` values are the four font paths; build clean.

### T3. `app.css` part 2: components, layout, utilities (after T2)

Append items 8 to 10 of 4.5: `@layer sv-components` (every class in 4.6 and the vault-door styles in 4.9), `@layer sv-layout` (4.6, including `#blazor-error-ui`), and `@layer sv-utilities` (utilities, `pointer: coarse`, `forced-colors`, `print`). Use tokens only; the only colour literals allowed outside the token blocks are `#FFFFFF` and `#000000` in `@media print`, `rgba(255, 255, 255, 0.25)` in `.sv-btn--primary`, and the system colours `CanvasText` and `Highlight` in `forced-colors`.

**Acceptance**: no comments; no `@import`, `-webkit-text-security`, `content: attr(`, `[value` or `pointer-events: none`; `infinite` appears once (`.sv-busy__pointer`); the file is at most 40,960 bytes; build clean.

### T4. Theme components (parallel with T1 and T2; T3 not required)

Create, per 4.7, 4.8 and 4.9:

- `Components/ComponentIds.cs`
- `Components/Icons/IconName.cs`, `Components/Icons/Icon.razor`
- `Components/Brand/VaultDoorVariant.cs`, `Components/Brand/VaultDoor.razor`
- `Components/Common/SecurityStatus.cs`, `StatusBadge.razor`, `AlertKind.cs`, `Alert.razor`, `EmptyState.razor`, `DialogShell.razor`, `SecretFrame.razor`, `BusyIndicator.razor`
- `_Imports.razor`: add `@using SharpVault.UI.Shared.Components`, `@using SharpVault.UI.Shared.Components.Icons`, `@using SharpVault.UI.Shared.Components.Brand`, `@using SharpVault.UI.Shared.Components.Common` (each once).

Rules: no `style` attribute, `<style>` or `<script>`; no `MarkupString`; `ComponentIds.EnsureValid` on every required id parameter in `OnParametersSet`; `DialogShell` focuses with `ElementReference.FocusAsync()` (Blazor's built-in interop, no project JS); `async void` is not used (`HandleKeyDownAsync` returns `Task`).

**Acceptance**: build clean; `grep -rn "style=\|<style\|<script" src/SharpVault.UI.Shared/Components` finds nothing; every `IconName` member has a `case` in `Icon.razor`.

### T5. Layout shell and shared pages (after T0 and T4; parallel with T3 and T6)

Per 4.10 and 4.11, in `src/SharpVault.UI.Shared`:

- `Layout/MainLayout.razor` (replace), `Layout/SkipLink.razor`, `Layout/AppHeader.razor`, `Layout/SideNav.razor` (new), `Layout/AccountMenu.razor` (restyle).
- Delete `Layout/MainLayout.razor.css` and `Layout/AccountMenu.razor.css`.
- `Pages/Home.razor`, `NotFound.razor`, `AccessDenied.razor`, `SignedOut.razor`, `SignInError.razor`, `Account.razor` (restyle only: no behaviour, route, attribute or `@code` change, and every existing id and visible text kept).

**Acceptance**: build clean; the entra-sign-in Web tests (`AccountPageTests`, `AccountPageApiTests`, `WebAuthenticationTests`, `AccountEndpointTests`) and `WebHostTests` pass unchanged; no `style=`, `<style`, inline `<script` or `on*=` in any changed file; every `a`, `button`, `input`, `NavLink` has an `id`.

### T6. Web host (after T0 and T1; parallel with T5)

In `src/SharpVault.Web`:

- `Components/App.razor`: the head changes in 4.10. Nothing else changes (the vault plan later adds render modes here).
- `Components/Routes.razor`: the `NotAuthorized` content in 4.11.
- `Components/Pages/Error.razor`: the state panel in 4.11 (logic unchanged).
- `Components/Layout/ReconnectModal.razor` and `.razor.css`: per 4.11. `ReconnectModal.razor.js` unchanged.

**Acceptance**: build clean; `GET /` HTML contains `viewport-fit=cover`, `name="color-scheme"`, two `name="theme-color"` metas and the favicon link; the existing Web tests pass.

### T7. Unit, bUnit, source-scan and contrast tests (test-writer; after T1 to T5)

All tests in section 7.1 to 7.3 and the size budgets in 7.5. Support files:

- `tests/SharpVault.UnitTests/Support/Theme/UiSourceFiles.cs`: `internal static class` that enumerates files under `src/SharpVault.UI.Shared` and `src/SharpVault.Web` by extension, using `RepositoryRoot.Path` and skipping any path segment `bin` or `obj`.
- `Support/Theme/CssBlocks.cs`: a brace-matching reader that returns top-level blocks as `(string Prelude, string Body)` (the files have no comments and no braces inside strings, which 7.1 enforces). `Declarations(string body)` returns `name -> value` for `--*` custom properties at the top level of a block.
- `Support/Theme/ThemeStylesheet.cs`: `Load()` reads `app.css`; `DarkTokens` is the declarations of the first top-level block with prelude exactly `:root`; `LightTokens` is the declarations of the `:root` block inside the top-level block with prelude exactly `@media (prefers-color-scheme: light)`.
- `Support/Theme/WcagContrast.cs`: `Ratio(string hexA, string hexB)` with the formula in 4.4.
- `Support/Theme/PinnedThemeAssets.cs`: file names, sizes and SHA-256 values from 4.2.
- `Support/Theme/RazorTags.cs`: `[GeneratedRegex]` helpers that find opening tags with quote-aware matching: `<(?<name>button|a|input|select|textarea|summary|NavLink|InputText|InputTextArea|InputSelect|InputCheckbox|InputNumber|InputDate|InputRadio|InputFile)\b(?<attrs>(?:[^>"']|"[^"]*"|'[^']*')*)>` (case-sensitive, so `<abbr>` and `<AuthorizeView>` do not match).
- bUnit test classes inherit `BunitContext`, set `JSInterop.Mode = JSRuntimeMode.Loose`, and live in `tests/SharpVault.UnitTests/UiShared/Theme/`. Layout and page tests call `AddAuthorization()` and `SetAuthorized("Ada Lovelace")` where needed, and substitute `ISharpVaultApiClient` with NSubstitute for `Account`.

### T8. Web integration tests (test-writer; after T5 and T6)

Section 7.4 in `tests/SharpVault.IntegrationTests/WebTests/ThemeWebTests.cs`, collection `WebTestGroup`. No Docker needed for anonymous pages.

### T9. Performance tests (test-writer; after T6)

Section 7.6 in `tests/SharpVault.IntegrationTests/WebTests/ThemePerformanceTests.cs`. Add `Factories/WebPerformanceTestGroup.cs`: `[CollectionDefinition(nameof(WebPerformanceTestGroup), DisableParallelization = true)] public sealed class WebPerformanceTestGroup : ICollectionFixture<WebTestFactory>;`

### T10. Documentation (docs-writer; after all)

- `docs/THEME.md` (new): the concept (4.1 summary), fonts and licences (4.2), the token tables (4.3), the contrast rule (4.4), the stylesheet layers (4.5), the component list (4.7), the CSP rules (4.14), the contract table (4.15), MAUI notes (4.13) and how to add an icon (copy inner elements from a pinned Lucide version, add an `IconName` member last, add a notice line if the source changes).
- `docs/TESTING.md`: the theme test helpers (T7 support files), `WebPerformanceTestGroup`, and that the size budget tests run in every pull request.
- `docs/PROGRESS.md`: Done entry for `feature/app-theme`; follow-ups: drop `data:` from `img-src` (D8), Playwright skip-link and dialog focus checks (D6, D14), MAUI iOS 15.4 minimum and `index.html` head (4.13), and the vault-plan adjustments in D10.
- `README.md`: one sentence in the UI section about the "Vault Steel" theme, linking `docs/THEME.md`.

## 6. Threats

There are no new endpoints. The surfaces are: the shared layout and the pages in 4.11 (anonymous: Home, NotFound, AccessDenied, SignedOut, SignInError; authorized: Account), the Web `Error` page and reconnect modal, the static assets `/_content/SharpVault.UI.Shared/css/app.css`, `/fonts/*` and `/brand/favicon.svg` (anonymous by design, V11), and the theme components that later features render secrets with. Assume every threat exists until its test passes.

| # | Category | Threat | Mitigation | Proving tests |
|---|---|---|---|---|
| TT1 | Tampering (XSS hardening) | The theme adds inline `style` attributes, `<style>` blocks, inline scripts or `on*` handlers. They would be blocked, and the "fix" would be to loosen the CSP to `'unsafe-inline'`, which also weakens OTL-3 | No inline style or script anywhere; dynamic visuals via classes and `data-*` (4.14); CSP unchanged | `RazorSources_UiProjects_ContainNoInlineStyleScriptOrHandlers`, `ThemeComponents_AnyRender_EmitNoStyleAttributeOrScript`, `ThemedPages_Rendered_ContainNoInlineScriptStyleOrHandlers`, `ForWeb_Theme_KeepsStyleAndFontSourcesSelfOnly` |
| TT2 | Leakage (privacy), supply chain | Fonts or icons loaded from a CDN (Google Fonts, jsDelivr): every page view leaks the user's IP and timing to a third party, and a compromised CDN can change what renders | Self-hosted fonts and inline SVG; scans for external URLs in CSS and Razor; rendered pages checked | `StyleSheets_UiProjects_ReferenceOnlyBundledFonts`, `RazorSources_UiProjects_ReferenceNoExternalOrigins`, `ThemedPages_Rendered_LoadNoExternalOrigins`, `ThemedPages_Rendered_HaveNoResourceHints` |
| TT3 | Tampering (supply chain) | A swapped or malicious font file (font parsers have had RCE bugs) or icon markup with `<script>`, `<use href>` or `<foreignObject>` | Fonts pinned by size and SHA-256 from integrity-checked tarballs; the font directory holds exactly the pinned files; icons are basic shapes only; the favicon has no script, style or links | `FontFiles_Bundled_MatchPinnedSizeAndSha256`, `FontFiles_Bundled_HaveWoff2Signature`, `FontDirectory_Contents_AreExactlyThePinnedFiles`, `UiSharedWebRoot_Files_HaveOnlyExpectedTypes`, `Icon_EveryName_RendersOnlyBasicShapes`, `Favicon_File_ContainsNoScriptStyleOrLinks` |
| TT4 | Leakage | CSS exfiltration of input values (attribute selectors such as `input[value^="a"]` with a background `url()`), or CSS that renders or masks a secret (`content: attr(...)`, `-webkit-text-security`, which leaves the real value in the DOM) | No `url()` except bundled fonts, no `[value` selectors, no `attr()` in `content`, no text-security; `SecretFrame` never renders the value while hidden | `StyleSheets_UiProjects_ReferenceOnlyBundledFonts`, `StyleSheets_UiProjects_UseNoValueAttributeSelectors`, `StyleSheets_UiProjects_NeverRenderOrMaskValuesWithCss`, `SecretFrame_Hidden_DoesNotRenderContent` |
| TT5 | Leakage (SEC-3) | The mask reveals the secret's length; a revealed value is sent to a cloud translation service (Chrome or Edge page translation) or a cloud spellchecker; JetBrains Mono ligatures merge `!=` or `->` and mislead the user; a revealed value is printed | Fixed eight-bullet mask; `translate="no"` and `notranslate` on values; secret inputs require `spellcheck="false"`, `autocapitalize="off"`, `autocorrect="off"` and explicit `autocomplete`; ligatures off for mono; print hides revealed values | `SecretFrame_HiddenWithShortOrLongContent_RendersIdenticalMask`, `SecretFrame_AnyState_ValueOptsOutOfTranslation`, `RazorSources_SecretInputs_DisableSpellcheckAutocorrectAndAutocapitalize`, `StyleSheets_MonospaceText_DisablesLigatures`, `StyleSheets_Print_HidesRevealedValues` |
| TT6 | Tampering (clickjacking) | Themed pages or assets framed by another site | `frame-ancestors 'none'` and `X-Frame-Options: DENY` on every page and asset (existing middleware); nosniff and same-origin CORP on assets | `ThemedPages_Response_HaveWebSecurityHeaders`, `ThemeAssets_AnonymousGet_ServeWithNoSniffAndSameOriginPolicy` |
| TT7 | Tampering (in-app UI redress) | A double-click or a stray Enter confirms a destructive dialog because focus lands on the confirm button; a backdrop that lets clicks through; a translucent idle-lock screen that shows the vault behind it | `DialogShell` focuses the dialog container, never a button; Cancel first in DOM; the backdrop covers the viewport and has no `pointer-events: none`; the lock screen background is the opaque `--color-bg` | `DialogShell_Opened_FocusesDialogNotAButton`, `DialogShell_Closed_RendersNothing`, `DialogStyles_Backdrop_CoversViewportAndBlocksPointer`, `LockScreenStyles_Background_IsOpaque` |
| TT8 | Escalation by confusion (LCK-1, ADM-4) | Status shown by colour alone (WCAG 1.4.1): a locked user looks active to a colour-blind admin; hidden and revealed look alike; error fields only turn red; the current nav item only changes colour; an unset status defaults to "Active" | Every status has its own icon and text; the default status is `Unknown`; alerts carry a text kind prefix; revealed adds an eye icon, the word "Visible" and a 2 px frame; validation messages start with "Error: "; current nav has a bar and bold weight | `StatusBadge_EveryStatus_RendersIconAndTextLabel`, `StatusBadge_Statuses_HaveDistinctIcons`, `StatusBadge_DefaultStatus_IsUnknownNotActive`, `StatusBadge_BlankLabel_FallsBackToDefaultText`, `Alert_EveryKind_RendersIconAndKindText`, `SecretFrame_Revealed_RendersVisibleLabelEyeIconAndContent`, `SecretFrame_Hidden_RendersHiddenLabelAndLockIcon`, `SecretFrame_RevealedNotSet_DefaultsToHidden`, `StyleSheets_ValidationMessages_HaveTextPrefix`, `StyleSheets_CurrentNavigation_HasNonColourIndicator` |
| TT9 | Misuse (accessibility) | Text or controls below WCAG AA in one scheme; an invisible focus ring; borders lost in Windows high contrast | Contrast computed from the tokens for 100 pairs; focus token with offset; forced-colours rules | `ThemeContrast_TokenPair_MeetsWcagAa`, `ThemeTokens_LightScheme_DefinesEveryDarkColourToken`, `ThemeTokens_ColourTokens_AreOpaqueHex`, `StyleSheets_FocusVisible_UsesFocusTokenWithOffset`, `StyleSheets_ForcedColours_IsSupported`, `WcagContrast_KnownPairs_MatchReferenceValues` |
| TT10 | Misuse (icon-only controls) | An icon-only button without an accessible name, so a screen-reader user presses "button" and deletes something | `sv-btn--icon` requires `aria-label`; decorative icons are `aria-hidden`; labelled icons have `role="img"` and a title | `RazorSources_IconOnlyButtons_HaveAccessibleName`, `Icon_WithoutLabel_IsHiddenFromAssistiveTechnology`, `Icon_WithLabel_HasImageRoleAndTitle` |
| TT11 | Harm (vestibular), resource use | Unwanted motion, endless animations | Reduced-motion override with layered `!important`; `infinite` only for the busy dial and the reconnect rings; the door animates only when sealing | `StyleSheets_App_HonoursReducedMotion`, `StyleSheets_InfiniteAnimations_OnlyInBusyIndicatorAndReconnect`, `VaultDoor_Default_IsDecorativeAndStatic`, `VaultDoor_Sealing_AddsSealingClass` |
| TT12 | Test integrity | Missing, unstable or duplicated ids, so Page Objects act on the wrong element (for example "delete" instead of "cancel") | Every interactive element has an id; component ids are validated kebab-case derived from a prefix | `RazorSources_InteractiveElements_HaveIds`, `ComponentIds_InvalidValue_Throws`, `ComponentIds_ValidValue_IsReturned`, `DialogShell_InvalidIdPrefix_Throws`, `SecretFrame_InvalidIdPrefix_Throws`, `MainLayout_Rendered_HasUniqueIds` |
| TT13 | Leakage | Themed state pages or the error bar show exception details or user data on anonymous pages | Restyle only; texts unchanged; existing leak tests re-run | `UnhandledException_Web_RendersErrorPageWithoutExceptionDetails` (existing), `AnonymousPages_Get_ShowNoUserDataAndHaveSecurityHeaders` (existing), `ThemedPages_Rendered_ContainNoInlineScriptStyleOrHandlers` |
| TT14 | Denial of service (client) | A huge stylesheet, an `@import` chain or large fonts delay first paint on slow mobile links; the theme assets are put behind sign-in, so anonymous pages render unstyled | Size budgets; no `@import`; fonts lazy by unicode range and `swap`; `MapStaticAssets().AllowAnonymous()` stays | `ThemeStylesheet_RawSize_IsWithinBudget`, `ThemeStylesheet_GzipSize_IsWithinBudget`, `FontFiles_TotalSize_IsWithinBudget`, `StyleSheets_UiProjects_UseNoImport`, `ThemedPages_Rendered_StylesheetAndScriptCountsWithinBudget`, `ThemeAssets_AnonymousGet_ServeWithNoSniffAndSameOriginPolicy` |

**Authorization matrix**: this work adds no endpoint and changes no `[Authorize]` attribute, policy or route. The entra-sign-in matrix and page tests (`WebAuthenticationTests`, `AccountPageTests`, `AccountPageApiTests`, `AccountEndpointTests`) must pass unchanged, and T5's acceptance requires it. Static theme assets are deliberately anonymous; `UiSharedWebRoot_Files_HaveOnlyExpectedTypes` proves nothing but CSS, fonts, licences and the favicon is published there.

## 7. Tests

Conventions: `Scenario_Condition_ExpectedResult`; security tests have `[Trait("Category", "Security")]`, timing tests `[Trait("Category", "Performance")]`; integration classes have `[Collection(...)]`; xUnit `Assert` only; `TestContext.Current.CancellationToken` for async calls.

### 7.1 Source scans: `tests/SharpVault.UnitTests/UiShared/Theme/ThemeSourceScanTests.cs` (Security)

Each failure message lists the file and the offending text.

- `RazorSources_UiProjects_ContainNoInlineStyleScriptOrHandlers`: every `.razor` in UI.Shared and Web has no `\sstyle\s*=`, no `<style`, no `<script` without `src=`, no `\son[a-z]+\s*=` (Blazor `@onclick` is preceded by `@`, so it does not match) and no `javascript:`.
- `RazorSources_UiProjects_ReferenceNoExternalOrigins`: no `(src|href|action|formaction|poster|xlink:href)\s*=\s*"(https?:)?//` in `.razor` files.
- `RazorSources_InteractiveElements_HaveIds`: every tag matched by `RazorTags` has `\sid\s*=`.
- `RazorSources_SecretInputs_DisableSpellcheckAutocorrectAndAutocapitalize`: every matched `input`, `textarea` or `Input*` tag whose attributes contain `type="password"` or a class with `sv-input--secret` also has `spellcheck="false"`, `autocapitalize="off"`, `autocorrect="off"` and `autocomplete=`. (Passes trivially today, and protects the vault editor.)
- `RazorSources_IconOnlyButtons_HaveAccessibleName`: every tag whose `class` contains `sv-btn--icon` has `aria-label=`.
- `StyleSheets_UiProjects_ReferenceOnlyBundledFonts`: in every `.css` under UI.Shared and Web, every `url\(([^)]*)\)` value is exactly `"../fonts/{name}.woff2"` where `{name}` is one of the pinned woff2 files, and only in `app.css`.
- `StyleSheets_UiProjects_UseNoImport`: no `@import`.
- `StyleSheets_UiProjects_ContainNoComments`: no `/*`.
- `StyleSheets_UiProjects_UseNoValueAttributeSelectors`: no `[value`.
- `StyleSheets_UiProjects_NeverRenderOrMaskValuesWithCss`: no `-webkit-text-security`, `text-security`, `content: attr(` or `content:attr(`.
- `StyleSheets_UiProjects_UseNoLegacyScriptingFeatures`: no `expression(`, `-moz-binding`, `behavior:`.
- `StyleSheets_InfiniteAnimations_OnlyInBusyIndicatorAndReconnect`: `infinite` appears only in `app.css` (exactly once, inside the `.sv-busy__pointer` rule) and in `ReconnectModal.razor.css`.
- `StyleSheets_App_HonoursReducedMotion`: `app.css` has a `@media (prefers-reduced-motion: reduce)` block inside `@layer sv-reset` that sets `animation-duration`, `animation-iteration-count`, `transition-duration` with `!important` for `*, *::before, *::after`.
- `StyleSheets_MonospaceText_DisablesLigatures`: the rules for `.sv-mono`, `.sv-secret__value`, `.sv-input--secret` and `code, kbd, samp, pre` each contain `font-variant-ligatures: none` and `"calt" 0`.
- `StyleSheets_Print_HidesRevealedValues`: a `@media print` block contains `.sv-secret[data-state="revealed"] .sv-secret__value` with `visibility: hidden`.
- `StyleSheets_ValidationMessages_HaveTextPrefix`: a rule for `.validation-message::before` (or a selector list containing it) sets `content: "Error: "`.
- `StyleSheets_CurrentNavigation_HasNonColourIndicator`: the rules for `.sv-nav__link.active` and `.sv-nav__link[aria-current="page"]` set `box-shadow: inset` and a `font-weight` of at least 650.
- `StyleSheets_FocusVisible_UsesFocusTokenWithOffset`: a `:focus-visible` rule sets `outline: 2px solid var(--color-focus)` and `outline-offset: 2px`.
- `StyleSheets_ForcedColours_IsSupported`: a `@media (forced-colors: active)` block exists and mentions `CanvasText` and `Highlight`.
- `DialogStyles_Backdrop_CoversViewportAndBlocksPointer`: the `.sv-dialog-backdrop` rule has `position: fixed` and `inset: 0`, and no rule in `app.css` contains `pointer-events: none`.
- `LockScreenStyles_Background_IsOpaque`: the `.sv-lockscreen` rule has `position: fixed`, `inset: 0` and `background: var(--color-bg)`, and does not reference `--color-overlay`.
- `FontFiles_Bundled_MatchPinnedSizeAndSha256` (Theory over the six pinned files): size and SHA-256 equal 4.2.
- `FontFiles_Bundled_HaveWoff2Signature` (Theory over the four woff2): first four bytes are `wOF2`.
- `FontDirectory_Contents_AreExactlyThePinnedFiles`: the directory holds the six pinned files and nothing else.
- `UiSharedWebRoot_Files_HaveOnlyExpectedTypes`: every file under `src/SharpVault.UI.Shared/wwwroot` has extension `.css`, `.woff2`, `.txt` or `.svg`.
- `Favicon_File_ContainsNoScriptStyleOrLinks`: `favicon.svg` has no `<script`, `<style`, `href`, `<foreignObject`, `on[a-z]+=` and is at most 1,024 bytes.
- `StyleSheets_FontFaces_UseSwapAndBundledFiles`: `app.css` has exactly four `@font-face` blocks, each with `font-display: swap`, a pinned `url(...)` and no `local(`.
- `ForWeb_Theme_KeepsStyleAndFontSourcesSelfOnly` (in `tests/SharpVault.UnitTests/ServiceDefaults/SecurityHeadersPolicyTests.cs`): for `SecurityHeadersPolicy.ForWeb(new Uri("https://login.microsoftonline.com/"))`, the `style-src` directive is exactly `'self'`, `font-src` exactly `'self'`, `script-src` exactly `'self'`, and the policy contains neither `unsafe-inline` nor `unsafe-eval`.

### 7.2 Tokens and contrast: `UiShared/Theme/ThemeContrastTests.cs` (Security)

- `ThemeContrast_TokenPair_MeetsWcagAa` (Theory, `MemberData` of 100 rows `(scheme, foreground, background, minimum)` from 4.4): the ratio from `ThemeStylesheet` tokens is at least the minimum. The failure message shows the ratio to two decimals.
- `ThemeTokens_LightScheme_DefinesEveryDarkColourToken`: the sets of `--color-*` and `--shadow-*` names are equal in both schemes, and the light block declares no other custom property.
- `ThemeTokens_ColourTokens_AreOpaqueHex`: every `--color-*` value in both schemes matches `^#[0-9A-F]{6}$`, except names ending in `-grain` or `-overlay`, which match `^rgba\(`.
- `ThemeTokens_RequiredTokens_AreDefined`: every token listed in 4.3 exists in `DarkTokens`.
- `WcagContrast_KnownPairs_MatchReferenceValues` (Theory): `#000000`/`#FFFFFF` is 21.00, `#777777`/`#FFFFFF` is 4.48 (two decimals), a colour on itself is 1.00, and the argument order does not matter.

### 7.3 bUnit component, layout and page tests (`UiShared/Theme/`)

`IconTests`
- `Icon_EveryName_RendersSvgWithChildren` (Theory over `Enum.GetValues<IconName>()`): one `svg.sv-icon` with `viewBox="0 0 24 24"` and at least one child shape.
- `Icon_EveryName_RendersOnlyBasicShapes` (Theory, Security): every descendant element name is in `path, rect, circle, line, polyline, polygon, ellipse, title`; no attribute named `style`, `href`, `xlink:href` or starting with `on`.
- `Icon_EveryName_UsesCurrentColour` (Theory): `stroke="currentColor"` on the `svg`.
- `Icon_WithoutLabel_IsHiddenFromAssistiveTechnology` (Security): `aria-hidden="true"`, `focusable="false"`, no `role`, no `title`.
- `Icon_WithLabel_HasImageRoleAndTitle`: `role="img"`, `aria-label` equals the label, first child is `<title>` with the label; no `aria-hidden`.

`VaultDoorTests`
- `VaultDoor_Default_IsDecorativeAndStatic`: `aria-hidden="true"`, classes `vault-door vault-door--hero`, no `vault-door--sealing`, no `id`.
- `VaultDoor_Sealing_AddsSealingClass`.
- `VaultDoor_MarkVariant_AddsMarkClass`.
- `VaultDoor_InvalidId_Throws`.

`StatusBadgeTests`
- `StatusBadge_EveryStatus_RendersIconAndTextLabel` (Theory, Security): an `svg.sv-icon` and non-empty `.sv-status__label` text, class `sv-status--{lowercase}`.
- `StatusBadge_Statuses_HaveDistinctIcons` (Security): the rendered icon markup differs for every pair of statuses.
- `StatusBadge_DefaultStatus_IsUnknownNotActive` (Security): with no `Status`, the label is "Unknown" and the class is `sv-status--unknown`.
- `StatusBadge_BlankLabel_FallsBackToDefaultText` (Theory: null, "", "   ").
- `StatusBadge_CustomLabelAndIcon_AreRendered` (Warning, "Lock scheduled", `ScheduledLock`).

`AlertTests`
- `Alert_DangerOrWarning_UsesAlertRole` (Theory).
- `Alert_InfoOrSuccess_UsesStatusRole` (Theory).
- `Alert_EveryKind_RendersIconAndKindText` (Theory, Security): an icon and the visually hidden prefix "Information: ", "Success: ", "Warning: " or "Error: ".
- `Alert_InvalidId_Throws`.

`EmptyStateTests`
- `EmptyState_Rendered_HasIdTitleAndLabelledBy`: `section#{Id}`, `aria-labelledby="{Id}-title"`, `h2#{Id}-title`.
- `EmptyState_WithoutActions_RendersNoActionsContainer`.

`DialogShellTests`
- `DialogShell_Closed_RendersNothing` (Security).
- `DialogShell_Open_HasDialogRoleModalAndLabelledByTitle`: `role="dialog"`, `aria-modal="true"`, `aria-labelledby="{p}-title"`, `tabindex="-1"`.
- `DialogShell_Open_IdsDerivedFromIdPrefix`: `{p}-backdrop`, `{p}-dialog`, `{p}-title`.
- `DialogShell_Opened_FocusesDialogNotAButton` (Security): render closed with a `Footer` holding two buttons, set `Open` to true, then `JSInterop.VerifyFocusAsyncInvoke().Arguments[0]` `ShouldBeElementReferenceTo` the `#{p}-dialog` element.
- `DialogShell_EscapePressed_InvokesOnCancel`: `KeyDown(new KeyboardEventArgs { Key = "Escape" })` on the dialog invokes `OnCancel` once; another key does not.
- `DialogShell_Danger_AddsDangerModifierAndIcon`.
- `DialogShell_InvalidIdPrefix_Throws` (Theory: null, "", "Upper", "has space", "1x", "a/b", Security).

`SecretFrameTests` (Security)
- `SecretFrame_RevealedNotSet_DefaultsToHidden`: `data-state="hidden"`, text "Hidden".
- `SecretFrame_Hidden_DoesNotRenderContent`: with `ChildContent` "SV-SECRET-MARKER-91c2", the marker is absent from the markup.
- `SecretFrame_HiddenWithShortOrLongContent_RendersIdenticalMask`: markup of `#{p}` with content "a" equals that with a 128-character content, and the mask text equals `SecretFrame.MaskText` (eight U+2022).
- `SecretFrame_Hidden_RendersHiddenLabelAndLockIcon`.
- `SecretFrame_Revealed_RendersVisibleLabelEyeIconAndContent`: `data-state="revealed"`, text "Visible", the eye icon markup, and the marker inside `#{p}`.
- `SecretFrame_AnyState_ValueOptsOutOfTranslation` (Theory: true, false): `#{p}` has `translate="no"` and class `notranslate`.
- `SecretFrame_Ids_DerivedFromIdPrefix`: `{p}-frame`, `{p}-state`, `{p}`.
- `SecretFrame_InvalidIdPrefix_Throws`.

`BusyIndicatorTests`
- `BusyIndicator_Rendered_HasStatusRoleAndTextLabel`.

`ComponentIdsTests`
- `ComponentIds_InvalidValue_Throws` (Theory: null, "", " ", "Upper", "has space", "1start", "a/b", "a_b"); the message does not contain the value.
- `ComponentIds_ValidValue_IsReturned` (Theory: "secret-value", "folder-node-3f2504e0-4f89-11d3-9a0c-0305e82c3301").

`ThemeMarkupTests`
- `ThemeComponents_AnyRender_EmitNoStyleAttributeOrScript` (Theory over one representative render of each component in 4.7, revealed and open where relevant; Security): no `style` attribute, no `<style`, no `<script`.

`LayoutTests` (after T5)
- `MainLayout_Rendered_HasSkipLinkHeaderAndMainLandmark`: `#skip-to-content`, `header#app-header`, `main#main-content[tabindex="-1"]`, `#app-shell`.
- `MainLayout_Rendered_HasUniqueIds` (authorized; Security): no `id` value repeats.
- `MainLayout_Rendered_ErrorUiDismissIsLabelledButton`: `#blazor-error-dismiss` is a `button` with class `dismiss` and `aria-label="Dismiss"`; `#blazor-error-reload` has class `reload`.
- `SkipLink_OnNestedRoute_TargetsMainContentOnSamePage`: after `NavigationManager.NavigateTo("account")`, `href` is `account#main-content`; at the root it is `#main-content`.
- `SkipLink_LocationChanges_UpdatesHref`.
- `SkipLink_Disposed_UnsubscribesFromLocationChanged`: after disposing the component, navigating does not throw and does not re-render it.
- `SideNav_SignedOut_RendersNothing`.
- `SideNav_SignedIn_EveryLinkHasIdIconAndText`.
- `AppHeader_Rendered_BrandLinkHasTextAndDecorativeMark`: `#brand-home-link` contains the text "SharpVault" and an `svg` with `aria-hidden="true"`.
- `AccountMenu_SignedIn_KeepsIdsAndTexts`: `#account-name` holds the name directly, `#account-link` text "Account", `#sign-out-button` contains "Sign out", form `#sign-out-form` is kept.
- `AccountMenu_SignedOut_RendersPrimarySignInLink`.

`PageTests` (after T5)
- `HomePage_SignedOut_RendersStaticHeroDoorAndPrimarySignIn`: `.sv-hero` has a `vault-door--hero` without `vault-door--sealing`; `#home-sign-in` has `sv-btn--primary`.
- `HomePage_SignedIn_KeepsGreeting`: `id="home-greeting"` text starts with "Signed in as".
- `SignedOutPage_Rendered_ShowsSealingVaultDoor`.
- `NotFoundPage_Rendered_UsesStatePanelWithHomeLink`.
- `AccessDeniedPage_Rendered_UsesDangerStateWithIconAndText`.
- `SignInErrorPage_Rendered_UsesDangerStateWithRetryLink`.
- `AccountPage_AdminProfile_RendersMonospaceIdAndAdminBadgeWithText` (NSubstitute `ISharpVaultApiClient` returning a profile): `#account-user-id` has `sv-mono` and directly contains the user id; `#account-display-name` directly contains the name; `#account-admin-badge` text "Administrator".
- `AccountPage_ApiFailure_RendersDangerAlertWithExistingId`: `#account-error` has `role="alert"`.

### 7.4 Web integration: `tests/SharpVault.IntegrationTests/WebTests/ThemeWebTests.cs` (collection `WebTestGroup`)

Routes theory data `ThemedRoutes`: `("/", 200)`, `("/does-not-exist", 404)`, `("/account/access-denied", 200)`, `("/account/signed-out", 200)`, `("/account/sign-in-error", 200)`. Each test asserts the status first.

- `ThemedPages_Response_HaveWebSecurityHeaders` (Theory, Security): `SecurityHeaderAssertions.AssertWeb(response)`.
- `ThemedPages_Rendered_ContainNoInlineScriptStyleOrHandlers` (Theory, Security): no inline `<script>` (the foundation regex), no `\sstyle\s*=`, no `<style`, no `\son[a-z]+\s*=`, no `importmap`, no `javascript:`.
- `ThemedPages_Rendered_LoadNoExternalOrigins` (Theory, Security): no `(src|href|action)="(https?:)?//`.
- `ThemedPages_Rendered_HaveNoResourceHints` (Theory, Security): no `rel="preconnect"`, `rel="dns-prefetch"`, `rel="prefetch"` or `rel="preload"`.
- `ThemedPages_Rendered_DeclareViewportColourSchemeAndIcon` (Theory): `viewport-fit=cover`, `<meta name="color-scheme" content="dark light"`, two `name="theme-color"`, and `rel="icon"` with `type="image/svg+xml"` and an href starting `_content/SharpVault.UI.Shared/brand/favicon`.
- `ThemedPages_Rendered_LinkThemeStylesheet` (Theory): one `rel="stylesheet"` href starting `_content/SharpVault.UI.Shared/css/app`.
- `ThemeAssets_AnonymousGet_ServeWithNoSniffAndSameOriginPolicy` (Theory over `/_content/SharpVault.UI.Shared/css/app.css`, the four font paths, `/_content/SharpVault.UI.Shared/brand/favicon.svg`; Security): 200 without any cookie, the expected `Content-Type` (`text/css`, `font/woff2`, `image/svg+xml`), `X-Content-Type-Options: nosniff`, `Cross-Origin-Resource-Policy: same-origin`, `X-Frame-Options: DENY`, and a `Cache-Control` that does not contain `no-store` (V1).
- `ThemeFont_AnonymousGet_MatchesPinnedBytes`: the downloaded body of `archivo-latin-wdth-normal.woff2` has the pinned SHA-256 (the served file is the committed file).
- `Favicon_AnonymousGet_IsServedAndPageNoLongerRequestsFaviconIco`: `GET` of the favicon returns 200; rendered `/` contains the icon link.

### 7.5 Size budgets (unit tests, every pull request, no category): `UiShared/Theme/ThemeBudgetTests.cs`

- `ThemeStylesheet_RawSize_IsWithinBudget`: `app.css` at most 40,960 bytes.
- `ThemeStylesheet_GzipSize_IsWithinBudget`: compressed with `GZipStream` at `CompressionLevel.Optimal`, at most 10,240 bytes.
- `UiSharedIsolatedStyles_TotalSize_IsWithinBudget`: all `.razor.css` under UI.Shared together at most 12,288 bytes (0 after this work; later features share the budget).
- `FontFiles_EachWoff2_IsWithinBudget`: each at most 100,000 bytes.
- `FontFiles_TotalSize_IsWithinBudget`: the four woff2 files at most 240,000 bytes.
- `FontFiles_FirstViewLatinSet_IsWithinBudget`: `archivo-latin-wdth-normal.woff2` (the only font a Latin page without secrets needs) at most 100,000 bytes.
- `ThemedPages_Rendered_StylesheetAndScriptCountsWithinBudget` (integration, in `ThemeWebTests`, Theory over `ThemedRoutes`): at most 2 `rel="stylesheet"` links, at most 2 `<script` elements (each with `src`), and HTML at most 32,768 bytes.

### 7.6 Timing: `WebTests/ThemePerformanceTests.cs` (collection `WebPerformanceTestGroup`, Performance)

Each test sends 5 warm-up requests on the client it measures with and asserts their status, then measures each request with `Stopwatch.GetTimestamp()` and `Stopwatch.GetElapsedTime(started)`. Budgets are `private static readonly TimeSpan` fields; failure messages list every timing measured.

- `ThemeStylesheet_FiftyRequests_EachCompletesUnder50Milliseconds`
- `ThemeFont_FiftyRequests_EachCompletesUnder50Milliseconds` (`archivo-latin-wdth-normal.woff2`)
- `HomePage_FiftyAnonymousRequests_EachCompletesUnder150Milliseconds`
- `SignedOutPage_FiftyAnonymousRequests_EachCompletesUnder150Milliseconds`

### 7.7 Commands the reviewer runs

```bash
dotnet --info
docker info
dotnet build SharpVault.slnx
dotnet test SharpVault.slnx --filter "Category!=Performance"
dotnet test SharpVault.slnx --filter "Category=Security"
dotnet test SharpVault.slnx --filter "Category=Performance"
dotnet list SharpVault.slnx package --vulnerable --include-transitive
sha256sum src/SharpVault.UI.Shared/wwwroot/fonts/*
```

Manual check (not automated, D23): run the AppHost, open `/`, `/account/signed-out` and `/does-not-exist` in light and dark system themes and with "reduce motion" on, and check the browser console shows no CSP violation.

## 8. Performance budgets

| Item | Budget | Calls | Test |
|---|---|---|---|
| `app.css` | at most 40,960 bytes raw and 10,240 bytes gzip | 1 request, fingerprinted, immutable in production | `ThemeStylesheet_RawSize_IsWithinBudget`, `ThemeStylesheet_GzipSize_IsWithinBudget` |
| Isolated component CSS in UI.Shared | at most 12,288 bytes total (0 now, so no extra `@import`ed bundle request) | 0 now | `UiSharedIsolatedStyles_TotalSize_IsWithinBudget` |
| Fonts on a Latin page without secrets | 90,104 bytes (Archivo latin only), budget 100,000 | 1 request, `font-display: swap`, not render-blocking | `FontFiles_FirstViewLatinSet_IsWithinBudget` |
| Fonts on a Latin page with a secret or id | 130,508 bytes (adds JetBrains Mono latin) | 2 requests | `FontFiles_EachWoff2_IsWithinBudget` |
| All bundled fonts | 231,944 bytes, budget 240,000 (latin-ext files load only for such characters) | at most 4 requests | `FontFiles_TotalSize_IsWithinBudget` |
| Render-blocking resources | at most 2 stylesheets, both same origin; scripts only by `src`; no resource hints | | `ThemedPages_Rendered_StylesheetAndScriptCountsWithinBudget`, `ThemedPages_Rendered_HaveNoResourceHints`, `ThemedPages_Rendered_LoadNoExternalOrigins` |
| Page HTML (themed pages) | at most 32,768 bytes | | `ThemedPages_Rendered_StylesheetAndScriptCountsWithinBudget` |
| Favicon | at most 1,024 bytes; removes the `/favicon.ico` 404 page render | 1 request | `Favicon_File_ContainsNoScriptStyleOrLinks` |
| Serving `app.css` or a font | under 50 ms each after warm-up | 0 storage, 0 API | `ThemeStylesheet_FiftyRequests_EachCompletesUnder50Milliseconds`, `ThemeFont_FiftyRequests_EachCompletesUnder50Milliseconds` |
| Rendering Home or SignedOut (anonymous) | under 150 ms each after warm-up | 0 storage, 0 API (unchanged) | `HomePage_FiftyAnonymousRequests_EachCompletesUnder150Milliseconds`, `SignedOutPage_FiftyAnonymousRequests_EachCompletesUnder150Milliseconds` |

- **Storage and API calls**: the theme adds none. The Web host has no storage client; the Account page keeps its single API call.
- **Queries without a partition key**: none (no storage access).
- **Hot paths and benchmarks**: none. No BenchmarkDotNet work.

## 9. Risks and decisions

| # | Topic | Decision and reason |
|---|---|---|
| D1 | Fonts | **Self-hosted Archivo (variable, `wdth` and `wght`) and JetBrains Mono (variable `wght`)**, both OFL 1.1 without Reserved Font Names, from Fontsource 5.3.0 tarballs checked by integrity (V4). Archivo's width axis gives the engraved-plate look (125%) and readable body text (100%) from one file, which a system stack cannot do, and the UI skill rules out generic system fonts. JetBrains Mono separates `0/O` and `1/l/I`, which matters for secrets, and its ligatures are turned off. Rejected: system stacks (generic, render differently per OS, so secrets look different on MAUI and Web); Archivo `wght`-only (34,928 bytes, but loses the plate width); Google Fonts or a CDN (blocked by the CSP and a privacy leak). No `local()` and no `preload`: preloading would need the fingerprinted URL to match the CSS URL, which it does not (V2), and would fetch fonts that unicode ranges might skip. Cost: 90 KB on first view, cached afterwards. |
| D2 | Light and dark | Dark is the brand default; the theme follows `prefers-color-scheme` and has no toggle. A toggle needs either JS or a server-side preference on static pages, and nothing asks for it yet. A later setting can add `[data-theme]` blocks with the same token names. |
| D3 | Accent and warning | Brass (`#D9AE55` dark, `#7D5A0F` light) is the only accent. Warning is a saturated hazard orange that differs from brass in hue and saturation, and every warning also has the triangle icon and the word "Warning", so the similarity is never the only cue. Signal green was rejected as the accent because it would collide with the Active status. |
| D4 | One global stylesheet with cascade layers | The theme's styles (tokens, element defaults, design-system classes and the shell) are global, because they are the theme definition the skill puts in `app.css`, and `NavLink` and child components do not get the parent's CSS isolation scope. It also avoids the `@import` of an RCL scoped bundle. Later features use `.razor.css` for component-specific rules, which win because they are unlayered. Browser baseline: Chromium 108, Safari 15.4, Firefox 101 (cascade layers, `:focus-visible`, `dvh`). MAUI must target iOS 15.4 or later (4.13). |
| D5 | Narrow screens | No hamburger menu. The side nav becomes a horizontal strip under the header. A toggle needs state that static SSR pages cannot keep without JS, and the nav has only a few entries. |
| D6 | Dialogs | A CSS overlay with `role="dialog"` and `aria-modal="true"`, not `<dialog>.showModal()`, which needs JS. Without JS there is no focus trap: keyboard users can tab to the page behind. Accepted for now, with focus moved into the dialog, Escape to cancel and the backdrop blocking pointer input. Clicking the backdrop does not close the dialog, so unsaved input is not lost by accident. The Playwright follow-up (foundation D23) checks focus behaviour. If a trap is required later, the page content can get the `inert` attribute while a dialog is open. |
| D7 | Reduced motion | A layered `!important` rule in `sv-reset`. Layered important declarations beat unlayered normal ones, so the rule also stops animations in later `.razor.css` files. |
| D8 | CSP | Unchanged (entra-sign-in owns the current format). The theme needs `style-src 'self'`, `font-src 'self'` and `img-src 'self'` only. `img-src` still allows `data:`, which nothing uses after this work; removing it is a PROGRESS.md follow-up, so this branch does not conflict with entra-sign-in. |
| D9 | Merge order | `feature/entra-sign-in` is not merged yet. T1 to T4 touch only files it does not change (V11: its `app.css` equals `main`'s), so they can start now. T5, T6, T8 and T9 wait for T0, because they restyle its AccountMenu and account pages, and its `ForWeb(Uri)` signature changes the CSP test in 7.1. |
| D10 | Fit with `feature/vault-and-folders` (orchestrator: pass this to the vault implementers) | (a) Token names are kept, so "the existing CSS variables" in the vault plan still exist. (b) The vault nav links `nav-vault` and `nav-recycle-bin` go into `Layout/SideNav.razor` (inside its `AuthorizeView`), not directly into `MainLayout`. (c) `MaskedField` renders `SecretFrame` with `IdPrefix` set to its existing value id, so the vault ids are unchanged. (d) `ConfirmDialog` and the vault dialogs render `DialogShell` and keep their `{IdPrefix}-dialog` ids. (e) `secret-value-input` and `secret-notes-input` need `class="sv-input--secret"`, `spellcheck="false"`, `autocapitalize="off"`, `autocorrect="off"` and `autocomplete`, or `RazorSources_SecretInputs_DisableSpellcheckAutocorrectAndAutocapitalize` fails. (f) `generator-strength` uses `.sv-meter[data-level]` plus text, because a width style is blocked. (g) `generator-output` uses `SecretFrame` (always revealed) so it gets `translate="no"`. (h) Merge conflicts: `Directory.Packages.props` and the UnitTests project (`bunit` once), `_Imports.razor` (each `@using` once), `THIRD-PARTY-NOTICES.md` (keep every section). (i) Vault `.razor.css` files count toward the 12 KiB isolated-CSS budget; if they exceed it, the vault planner raises the budget with a reason. |
| D11 | Icons | Lucide path data embedded in one `Icon` component, rendered inline. Rejected: an icon font (needs a font file per glyph set and is inaccessible), an external SVG sprite with `<use href>` (an extra request, and `href` inside SVG is something the scans forbid), and drawing icons by hand (inconsistent). Inline markup costs about 150 to 500 bytes per icon in the HTML, inside the 32 KiB page budget. |
| D12 | Mask | Always eight bullets. A mask that matches the secret's length leaks information to anyone who can see the screen. |
| D13 | Where budgets run | Size budgets are deterministic, so they are ordinary unit tests and run on every pull request. Only timing tests carry `Category=Performance` and run on pushes to `main`, like the foundation's. |
| D14 | Skip link | `<base href="/">` makes `href="#main-content"` resolve to the home page on nested routes, so `SkipLink` builds the current relative path plus `#main-content` and refreshes on `LocationChanged`. Whether Blazor's enhanced navigation moves focus as well as scrolling is checked in the Playwright follow-up. |
| D15 | Error bar dismiss | Changed from a `span` to a labelled `button` with the same `dismiss` class. `blazor.web.js` selects `#blazor-error-ui .dismiss` regardless of element type (V8). |
| D16 | Fail-safe defaults | `SecretFrame.Revealed` defaults to false (hidden, SEC-3). `SecurityStatus` defaults to `Unknown`, so an unset status never shows "Active". `AlertKind` defaults to `Info`. |
| D17 | Static assets anonymous | Required so the signed-out, access-denied and error pages are styled. The scan `UiSharedWebRoot_Files_HaveOnlyExpectedTypes` keeps the anonymous web root free of anything but CSS, fonts, licences and the favicon. |
| D18 | Licence files in the web root | The OFL requires the licence to accompany the fonts, and serving it next to them is harmless. They are marked `-text` in `.gitattributes`, so their hashes are stable on Windows checkouts. |
| D19 | No automated browser accessibility scan | Playwright and axe are not available in this environment (foundation D23). The contrast test computes WCAG ratios from the actual tokens, and bUnit tests prove the non-colour cues, roles and labels. |

## 10. Definition of done for this branch

- T0 to T10 are complete. `dotnet build SharpVault.slnx` reports 0 warnings and 0 errors.
- Every test in section 7 exists and passes, including the Security and Performance categories, and every existing test (foundation, encryption core, entra-sign-in) still passes.
- Every threat in section 6 has its tests passing.
- The four woff2 files and two licences match section 4.2 byte for byte.
- No comments in any `.cs`, `.razor`, `.css` or `.js` file, no commented-out code and no TODOs. The AGPL header is on every new `.cs` file.
- `dotnet list SharpVault.slnx package --vulnerable --include-transitive` reports nothing.
- `THIRD-PARTY-NOTICES.md` credits Archivo, JetBrains Mono and Lucide (ISC and MIT text).
- The reviewer has approved. The pull request title is `feat: add Vault Steel app theme`. Its description says "UX foundation (no requirement id); supports SEC-3, ADM-4, LCK-1, OTL-3" and lists D10 for the vault branch.
