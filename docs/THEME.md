# Theme: Vault Steel

The SharpVault look is called Vault Steel: a machined vault at night. Surfaces are blued graphite and deep navy, like gun-blued steel. The header has a faint brushed-metal grain and a thin brass rim. Labels look like engraved plates (wide, tracked capitals). Brass is the one accent, and it marks only the parts of a vault door that are polished: the primary action, the focus ring, the current location and a revealed secret. Secret values sit in sunken wells and use a monospace face with ligatures off.

The theme is built once in `src/SharpVault.UI.Shared`, so the Web app and the planned MAUI app share it. Nothing in the theme is specific to one host.

## Where things are

| Path (under `src/SharpVault.UI.Shared/`) | What it is |
|---|---|
| `wwwroot/css/app.css` | The whole stylesheet: tokens, reset, base, components, layout and utilities, in cascade layers |
| `wwwroot/fonts/` | Four WOFF2 files and two licence files |
| `wwwroot/brand/favicon.svg` | The vault door favicon |
| `Components/` | Razor components: `Icon`, `Brand/VaultDoor`, `Common/StatusBadge`, `Alert`, `EmptyState`, `DialogShell`, `SecretFrame`, `BusyIndicator`, and `ComponentIds` |
| `Layout/` | `MainLayout`, `SkipLink`, `AppHeader`, `SideNav`, `AccountMenu` |
| `Pages/` | Home, NotFound, AccessDenied, SignedOut, SignInError, Account |

`THIRD-PARTY-NOTICES.md` in the repository root lists the fonts and icons with their licences.

## Fonts and licences

Both fonts are self-hosted. The stylesheet loads them from `../fonts/`. There is no CDN, no `local()` source and no preload.

| Role | Family | File | Licence |
|---|---|---|---|
| UI, body, headings, plate labels (Latin) | Archivo variable (`wdth` 62 to 125, `wght` 100 to 900) | `archivo-latin-wdth-normal.woff2` | SIL OFL 1.1 (`OFL-Archivo.txt`) |
| Same, Latin Extended | Archivo | `archivo-latin-ext-wdth-normal.woff2` | SIL OFL 1.1 |
| Secrets, identifiers, code (Latin) | JetBrains Mono variable (`wght` 100 to 800) | `jetbrains-mono-latin-wght-normal.woff2` | SIL OFL 1.1 (`OFL-JetBrainsMono.txt`) |
| Same, Latin Extended | JetBrains Mono | `jetbrains-mono-latin-ext-wght-normal.woff2` | SIL OFL 1.1 |

Both fonts come from the npm packages `@fontsource-variable/archivo` and `@fontsource-variable/jetbrains-mono` (version 5.3.0). The WOFF2 files are the unmodified package files, and their SHA-256 hashes are pinned in the theme plan (`docs/plans/app-theme.md`, section 4.2). The licence files are the packages' OFL texts, unmodified. Neither font declares a Reserved Font Name.

Rules:

- Every `@font-face` rule uses `font-style: normal` and `font-display: swap`. No italics are shipped. Emphasis uses weight, and `font-synthesis: none` stops faux italics.
- Secret and code text uses `font-variant-ligatures: none` and `font-feature-settings: "calt" 0, "liga" 0`, so `!=` or `->` inside a password are not merged.

Icons are inline SVG copied from `lucide-static` 1.66.0 (ISC licence, with an MIT notice for the Feather-derived icons). There are 31 icons, all listed in `Components/Icons/IconName.cs`.

## Design tokens

Tokens are CSS custom properties on `:root`. Dark is the default. The light scheme is set by `@media (prefers-color-scheme: light)`. The theme follows the operating system, and there is no in-app toggle.

Main colour tokens (dark value, then light value):

| Token | Dark | Light | Used for |
|---|---|---|---|
| `--color-bg` | `#0A1017` | `#ECEFF3` | page |
| `--color-surface` | `#111923` | `#F8FAFC` | header, side nav, cards, tables |
| `--color-surface-raised` | `#172230` | `#FFFFFF` | dialogs, buttons, current nav item |
| `--color-surface-sunken` | `#070B10` | `#E2E7ED` | inputs, secret wells, tags |
| `--color-border` | `#233140` | `#CBD3DC` | decorative dividers only |
| `--color-border-strong` | `#6B7E93` | `#6A7A8D` | control boundaries |
| `--color-text` | `#E7EBF0` | `#0E1620` | text |
| `--color-text-muted` | `#9AA7B6` | `#4B5869` | secondary text, plates |
| `--color-accent` | `#D9AE55` | `#7D5A0F` | brass: primary button, current marker, revealed frame |
| `--color-focus` | `#F0C66B` | `#7D5A0F` | focus ring |
| `--color-success` | `#5BD69A` | `#146C43` | Active |
| `--color-locked` | `#8DBDF0` | `#1F5A94` | Locked |
| `--color-warning` | `#FF9D57` | `#9A4300` | Warning, scheduled lock |
| `--color-danger` | `#FF7A7A` | `#B3242B` | errors, destructive outline |

Each status colour also has `-bg` and `-border` variants, and the danger fill has `-solid`, `-solid-hover` and `on-danger`. `--color-on-accent` is the text colour on brass. The `--color-grain` and `--color-overlay` tokens are `rgba(...)` values. All other colour tokens are opaque `#RRGGBB`.

Other tokens:

- **Shadows**: `--shadow-raised` (cards) and `--shadow-dialog` (dialogs and the error bar), with separate light and dark values.
- **Fonts**: `--font-sans` (Archivo) and `--font-mono` (JetBrains Mono).
- **Type sizes**: `--text-xs` to `--text-3xl`. The largest is fluid.
- **Spacing**: `--space-xs` to `--space-2xl`.
- **Radii**: `--radius-sm` (3px), `--radius-md` (6px), `--radius-lg` (10px) and `--radius-round`.
- **Motion**: `--duration-fast`, `--duration-normal`, `--duration-seal`, `--ease-out` and `--ease-lock`.
- **Layout**: `--header-height`, `--sidenav-width`, `--content-max`, `--control-height` and `--tap-target`.
- **Layers**: `--z-header`, `--z-dialog`, `--z-lock`, `--z-error-ui` and `--z-skip`.

Media queries cannot use variables. The breakpoints are fixed at `30rem`, `40rem` (dialogs become bottom sheets below this), `48rem` and `60rem` (side nav column).

## Contrast

Every text and UI colour pair meets its WCAG 2.2 AA minimum in both schemes:

- Text and accent text: 4.5:1.
- Text on filled controls (`on-accent`, `on-danger`): 4.5:1.
- Status text and icons, and text inside status containers: 4.5:1.
- Control boundaries (`border-strong`), the focus ring and filled control boundaries: 3:1.

`--color-border` is decorative. It is never the only boundary of a control. Disabled controls are exempt.

`ThemeContrastTests` computes the ratios from `app.css` itself, so a token change that breaks a pair fails the unit tests. Add a new pair to that test when you add a new text or control colour.

## Stylesheet structure

`app.css` has a fixed top-level order. The contrast test parser depends on it:

1. `@layer sv-reset, sv-base, sv-components, sv-layout, sv-utilities;`
2. The four `@font-face` rules.
3. `:root` with every token (dark values).
4. The light-scheme media block.
5. `sv-reset`: box sizing, form control inheritance, `[hidden]`, and the reduced-motion rule.
6. `sv-base`: body, headings, links, focus ring, selection, code, inputs, and the `.validation-message` and `.validation-errors` classes used by Blazor forms.
7. The four `@keyframes`: `sv-seal-wheel`, `sv-seal-bolts`, `sv-dial-tick` and `sv-reveal`.
8. `sv-components`: buttons, fields, cards, tables, lists, tags, status badges, alerts, secret frame, strength meter, dialogs, tree, empty and state panels, hero, busy indicator and lock screen.
9. `sv-layout`: skip link, frame, sticky header, side nav and main.
10. `sv-utilities`: utilities, forced colours and print.

Why layers: unlayered styles beat layered ones. A component's `.razor.css` (unlayered) therefore overrides a theme default without specificity fights. The reduced-motion rule is layered `!important`, so it beats every unlayered animation.

## Components and ids

Every interactive element has a stable `id`. Components take the id as a parameter and check it with `ComponentIds.EnsureValid`, which requires lowercase kebab-case (`^[a-z][a-z0-9-]*$`). It throws for anything else and does not echo the value.

| Component | Parameters | Notes |
|---|---|---|
| `Icon` | `Name` (`IconName`), `Label`, `CssClass` | Decorative unless `Label` is set. |
| `VaultDoor` | `Variant` (`Hero`, `Mark`), `Sealing`, `Id` | Brand mark and hero illustration. `Sealing` plays the wheel and bolt animation once, on sign-out. |
| `StatusBadge` | `Status` (`Unknown`, `Active`, `Locked`, `Warning`, `Danger`), `Label`, `Icon`, `Id` | Always text and icon, never colour alone. |
| `Alert` | `Id` (required), `Kind` (`Info`, `Success`, `Warning`, `Danger`), `Title` | `role="alert"` for Warning and Danger, `role="status"` otherwise. |
| `EmptyState` | `Id`, `Title`, `Icon`, `ChildContent`, `Actions` | |
| `DialogShell` | `IdPrefix`, `Title`, `Open`, `Danger`, `Footer`, `OnCancel` | Focuses the dialog, not a button. Escape calls `OnCancel`. A backdrop click does nothing. |
| `SecretFrame` | `IdPrefix`, `Revealed` (default hidden), `Actions` | Hidden state renders a fixed mask of eight bullets and never the content. Print hides revealed values. |
| `BusyIndicator` | `Id`, `Label` | `role="status"`. |

Fixed ids in the shell and pages:

| Id | Element |
|---|---|
| `skip-to-content` | Skip link |
| `app-header`, `brand-home-link` | Header and brand |
| `side-nav`, `nav-home` | Side navigation |
| `account-menu`, `account-link`, `account-name`, `sign-in-link`, `sign-out-form`, `sign-out-button` | Account menu |
| `app-frame`, `main-content`, `app-shell` | Layout and main landmark |
| `blazor-error-ui`, `blazor-error-reload`, `blazor-error-dismiss` | Blazor error bar |
| `home`, `home-heading`, `home-sign-in` | Home page |
| `account-section`, `account-heading`, `account-display-name`, `account-user-id`, `account-admin-badge` | Account page |
| `access-denied-heading`, `signed-out-heading`, `sign-in-error-heading`, `not-found-heading` | State pages |

Icons: `IconName` has 31 members. To add one, copy the inner SVG elements of the Lucide icon (not the outer `<svg>` or the license comment), add the member at the end of the enum, and add a notice line in `THIRD-PARTY-NOTICES.md` if the source version changes.

## Adding styles

1. Use the tokens. Do not write a new colour literal, `style` attribute or `<style>` element.
2. Shared classes go in the matching layer of `app.css`. Feature-specific styles go in a `.razor.css` file. That file is unlayered, so it overrides the theme, and it may use tokens only.
3. Dynamic visuals use classes or `data-*` attributes with a fixed set of rules, for example `.sv-meter[data-level="3"]`. Never compute a width in markup.
4. Use the existing components before writing markup. The contract table in `app-theme.md` (section 4.15) gives the rule for each need: secrets, dialogs, status, messages, empty lists, loading, navigation, folder trees, tags, password strength, the idle lock screen and icon-only buttons.
5. Icon-only buttons need `aria-label`. Secret inputs need `class="sv-input--secret"`, `spellcheck="false"`, `autocapitalize="off"`, `autocorrect="off"` and an explicit `autocomplete`.
6. Run `dotnet test SharpVault.slnx --filter "FullyQualifiedName~UiShared"` after any CSS change. The source scans, contrast and budget tests enforce the rules above.

## Content Security Policy

The Web host sends this policy on every page, with the Entra origin filled in:

```
default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' https://login.microsoftonline.com; upgrade-insecure-requests
```

The `form-action` entry is the identity provider origin (from `AzureAd:Instance`). The sign-out form posts to the app, and the browser then follows the redirect to Entra's logout endpoint.

What this means for UI work:

- No `style` attributes, no `<style>` elements and no `style` set from code. `style-src 'self'` blocks them.
- No inline `<script>`, no `on*` attributes and no `javascript:` URLs. `script-src 'self'` blocks them.
- No external origins in `src`, `href`, `action`, `formaction`, `poster` or CSS `url()`. Fonts and icons are local.
- No `<link rel="preconnect">`, `dns-prefetch`, `prefetch` or `preload`.
- SVG presentation attributes (`fill`, `stroke`, `transform`) are allowed. They are not inline styles.
- Do not loosen the policy to fit a feature. One-time links (OTL-3) depend on the strict policy.

The stylesheet rules that these checks enforce are in section 4.5 of `app-theme.md`. One follow-up remains: `img-src` still allows `data:`, which the theme plan lists for removal.

JavaScript: the theme adds no JavaScript. The Web host still has the Blazor reconnect script `Components/Layout/ReconnectModal.razor.js`, which comes from the Blazor template and runs under `script-src 'self'`. The JS interop rule in `CLAUDE.md` allows Blazor JS interop only for specific browser methods that .NET cannot call directly (for example the clipboard, or Web Crypto for one-time links), wrapped behind a C# interface. Dialog focus uses Blazor's own `ElementReference.FocusAsync()`.

## Motion, print and forced colours

- `prefers-reduced-motion: reduce` shortens every animation and transition to 0.01 ms and sets the iteration count to 1. The rule is in `sv-reset` as layered `!important`.
- The only infinite animations are the busy-dial pointer (`.sv-busy__pointer`) and the Web reconnect modal.
- The vault door animation plays once on sign-out. The idle lock screen (ADM-4) will use the same sealing door.
- The `forced-colors` and print rules are in `sv-utilities`. Print hides revealed secret values.

## MAUI notes

The planned MAUI app (`feature/maui-app`) must:

- link `_content/SharpVault.UI.Shared/css/app.css` from its `wwwroot/index.html`, so the fonts and stylesheet load through the same path as the Web;
- set `<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover" />` and `<meta name="color-scheme" content="dark light" />`;
- set the iOS and Mac Catalyst minimum to 15.4, because the stylesheet uses cascade layers, `:focus-visible` and `dvh`.

Safe-area insets (`env(safe-area-inset-*)`) are used by the header, main, side nav strip, dialog bottom sheet, lock screen and error bar.

## Tests

Theme tests are in `tests/SharpVault.UnitTests/UiShared/Theme/`:

| Test class | What it checks |
|---|---|
| `ThemeContrastTests` | Every contrast pair in both schemes, computed from `app.css` |
| `ThemeSourceScanTests` | The CSS and Razor rules above (no inline style, no external origin, no comments, no unsafe `url()`) |
| `ThemeMarkupTests` | Rendered HTML of the components and pages |
| `ThemeBudgetTests` | Size budgets for the stylesheet and fonts |
| `ThemeScanSupportTests` | The CSS parser used by the scans |
| `IconTests`, `VaultDoorTests`, `StatusBadgeTests`, `AlertTests`, `EmptyStateTests`, `DialogShellTests`, `SecretFrameTests`, `BusyIndicatorTests`, `ComponentIdsTests` | Component behaviour with bUnit |

The size budget tests run in every pull request, so a feature that bloats the stylesheet or adds a font fails the build.

## Not done yet

- Playwright checks for the skip link and dialog focus (foundation D23 and theme D6, D14). Browser checks need the E2E setup, which is a later feature.
- The MAUI `index.html` and iOS minimum (section above), which come with `feature/maui-app`.
- Removing `data:` from `img-src` (theme D8).
