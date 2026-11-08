# Theme: Vault Steel

The SharpVault look is called Vault Steel: a machined vault at night. Surfaces are blued graphite and deep navy. Brass is the one accent, and it marks the primary action, the focus ring and a revealed secret. Secret values sit in monospace text with ligatures off.

The UI is built on MudBlazor 9.11.0 (MIT). Vault Steel is a MudBlazor theme plus one small stylesheet. Both live in `src/SharpVault.UI.Shared`, so the Web app and the planned MAUI app share them.

## Where things are

| Path (under `src/SharpVault.UI.Shared/`) | What it is |
|---|---|
| `Theming/SharpVaultTheme.cs` | The MudBlazor theme: dark and light palettes, typography and layout sizes |
| `wwwroot/css/app.css` | The stylesheet for what MudBlazor does not cover: fonts, skip link, focus ring, state panels, secret wells, vault door, error bar, reduced motion, forced colours and print |
| `wwwroot/fonts/` | Four WOFF2 files and two licence files |
| `wwwroot/brand/favicon.svg` | The vault door favicon |
| `Components/Base/` | `SharpVaultComponentBase`, `LoadableComponentBase<T>`, `DataStateView<TValue>`, `LoadState` |
| `Components/Common/` | The `Sv*` wrappers and the state, status and secret components |
| `Components/Brand/` | `VaultDoor` |
| `Components/Icons/SvIcons.cs` | Icon names used by the components (MudBlazor Material icons) |
| `Components/Layout/` | `AppErrorBoundary` and `IdleLock` |
| `Components/Account/`, `Components/Home/`, `Components/Users/` | Feature components: `ProfileCard`, `HomeHero`, `UserDirectoryTable` |
| `Layout/` | `MainLayout`, `AppHeader`, `SideNav`, `AccountMenu`, `SkipLink` |
| `Pages/` | Home, Account, AdminUsers, AccessDenied, SignedOut, SignInError, NotFound, Error |
| `Services/` | `Auth/` (token types, `ISessionActions`, `ISessionValidator`), `Clients/` (API clients on `ApiClientBase`), `Users/` (`ICurrentUserAccessor`) and `Idle/` (idle monitor and options) |

The Web host also links `_content/MudBlazor/MudBlazor.min.css` and `MudBlazor.min.js` from `App.razor`. `THIRD-PARTY-NOTICES.md` in the repository root lists the fonts and the word list. It does not yet list MudBlazor (MIT) or the Material icon notice (Apache-2.0), see `docs/PROGRESS.md`.

## MudBlazor theme

`SharpVaultTheme.Instance` is the one theme. `MainLayout` passes it to `MudThemeProvider`.

- Dark is the default. After the first interactive render the layout asks the browser for the system scheme (`GetSystemDarkModeAsync`) and follows it. There is no in-app toggle.
- Typography: Archivo for text, buttons without upper-case and at weight 600.
- Layout: border radius 6px, drawer 15rem, app bar 3.5rem.

Palette values in the code (dark, then light):

| Palette role | Dark | Light |
|---|---|---|
| Primary (brass) | `#D9AE55` | `#7D5A0F` |
| Secondary (muted text) | `#9AA7B6` | `#4B5869` |
| Tertiary and Info (locked) | `#8DBDF0` | `#1F5A94` |
| Success (active) | `#5BD69A` | `#146C43` |
| Warning | `#FF9D57` | `#9A4300` |
| Error | `#E5484D` | `#B3242B` |
| Background | `#0A1017` | `#ECEFF3` |
| Surface and app bar | `#111923` | `#F8FAFC` |
| Divider | `#233140` | `#CBD3DC` |
| Text | `#E7EBF0` | `#0E1620` |

The palette is the colour source. The stylesheet has no colour tokens of its own. Its colours come from `var(--mud-palette-*)`, with two exceptions: the error bar shadow is a fixed `rgba` value, and the print rule sets fixed black text and a white background. The forced-colours block uses system colours (`CanvasText`, `Highlight`).

Contrast is not tested. The WCAG contrast tests were removed in the test cleanup, because they pinned colour, and the owner decided that appearance is reviewed by eye with the site tour. A palette change that breaks a pair is caught only by that review.

## Fonts and licences

Both fonts are self-hosted. `app.css` loads them from `../fonts/`. There is no CDN and no `local()` source.

| Role | Family | File | Licence |
|---|---|---|---|
| UI, body, headings (Latin) | Archivo variable | `archivo-latin-wdth-normal.woff2` | SIL OFL 1.1 (`OFL-Archivo.txt`) |
| Same, Latin Extended | Archivo | `archivo-latin-ext-wdth-normal.woff2` | SIL OFL 1.1 |
| Secrets and code (Latin) | JetBrains Mono variable | `jetbrains-mono-latin-wght-normal.woff2` | SIL OFL 1.1 (`OFL-JetBrainsMono.txt`) |
| Same, Latin Extended | JetBrains Mono | `jetbrains-mono-latin-ext-wght-normal.woff2` | SIL OFL 1.1 |

The fonts come from `@fontsource-variable/archivo` and `@fontsource-variable/jetbrains-mono` (5.3.0). The WOFF2 files are unmodified package files. Neither font declares a Reserved Font Name.

Rules that the tests enforce:

- Every `@font-face` uses `font-style: normal` and `font-display: swap`. No italics are shipped.
- The `.sv-mono` class and secret values use JetBrains Mono with `font-variant-ligatures: none` and `font-feature-settings: "calt" 0`, so `!=` or `->` inside a password are not merged.

## Stylesheet

`app.css` is trimmed to what the MudBlazor UI uses. It defines no design tokens of its own. It contains:

- the four `@font-face` rules;
- the focus ring (`:focus-visible`, 2px outline in `--mud-palette-primary`);
- the skip link and the visually hidden helper;
- the mono class, plate labels, brand, app bar, side nav, page header and hero rules;
- state panel, picker, account card, dialog, secret well and busy indicator rules;
- the vault door, with its sealing animation;
- the Blazor error bar (`#blazor-error-ui`);
- reduced motion, forced colours and print. Print hides revealed secret values.

Size is not tested. The stylesheet and font size budgets were removed in the test cleanup, because they pinned appearance. The MudBlazor stylesheet itself is about 625 KB raw and 67 KB gzip (figures from the earlier notes, not re-measured). The Web page response budgets (150 ms per page, fifty requests each) stay in `WebPagePerformanceTests`.

## Component tiers

| Tier | Where | What | Rule |
|---|---|---|---|
| 1. MudBlazor | NuGet package | Mud components, providers, icons | Used inside the wrappers. Some pages still use a few Mud primitives directly (see Next in `PROGRESS.md`). |
| 2. Wrappers | `Components/Common/`, `Components/Brand/` | `SvButton`, `SvIconButton`, `SvAlert`, `SvDialog`, `SvTable` with `SvColumn`, `StatePanel`, `StatusChip`, `SecretField`, `EmptyState`, `BusyIndicator`, `SignInLink`, `VaultDoor` | Take a stable kebab-case id. The wrappers check it with `ComponentIds.EnsureValid`, either directly or in the inner wrapper they pass it to (`EmptyState` passes its id to `StatePanel`, and `SignInLink` passes its id to `SvButton`). Forward only real Mud parameters and lowercase HTML attributes (`id`, `aria-*`, `data-*`). Never pass `Name` or `Value` to a Mud button (team rule; no test checks it). |
| 3. Base | `Components/Base/` | `SharpVaultComponentBase`, `LoadableComponentBase<T>`, `DataStateView<TValue>` | Pages that load data inherit `LoadableComponentBase<T>` and render through `DataStateView`. |
| 4. Layout and pages | `Layout/`, `Components/Layout/`, `Pages/` | The shell, the idle lock, the error boundary and the pages | Compose tiers 2 and 3. |

Wrapper behaviour:

- `SvButton` variants: `Primary` and `Danger` are filled (Primary or Error colour), `Secondary` is outlined, `Ghost` is text. `Busy` disables the button and shows a small spinner.
- `StatusChip` maps `Active` to Success, `Locked` to Info, `Warning` and `Danger` to Warning and Error. The text and icon always show, never colour alone.
- `SvDialog` takes `IdPrefix`, `Title`, `Open`, `Danger`, `ChildContent`, `Footer` and `OnCancel`.
- `SvTable` takes `Id`, `Items`, `Columns`, `EmptyContent` and `Loading`. Each `SvColumn` gives a `Header` and either a `Value` function or `ChildContent`. A column outside an `SvTable` throws.
- `SecretField` shows a fixed mask of eight bullets unless `Revealed` is set, and never the content. Print hides revealed values.

Never put a `style` attribute or a `Style` parameter on any element (see CSP below).

## Loading, empty, error and success

`LoadableComponentBase<T>` runs `LoadAsync` once on initialisation. A subclass can override `IsEmpty` to report an empty result. `ReloadAsync` starts again from Loading, and the Try again button calls it.

The four states are `LoadState.Loading`, `Empty`, `Error` and `Loaded`. `DataStateView<TValue>` renders one of them:

| State | Renders | Element ids |
|---|---|---|
| Loading | `LoadingSkeleton`, or the `Loading` fragment | `{prefix}-loading` |
| Empty | `EmptyState` with `EmptyTitle`, or the `Empty` fragment | `{prefix}-empty` |
| Error | `SvAlert` (danger) with `ErrorText`, a Try again button, or the `Error` fragment | `{prefix}-error`, `{prefix}-retry` |
| Loaded | `ChildContent(Value)` | (the content's own ids) |

The base maps each API result to a state:

| `ApiCallStatus` | Result |
|---|---|
| `Success` with a value | Loaded, or Empty when `IsEmpty` is true |
| `SignInRequired` | Full page navigation to sign-in, with the current page as `returnUrl` and the retry flag. If the page already has the retry flag, the state is Error with `SignInLoopText`. |
| `AccessDenied` | Clears the current-user cache and navigates to the access-denied page |
| `ConfigurationMismatch` | Error with `ConfigurationMismatchText` (the web app and the API disagree about the mode or the key) |
| `Failed`, or any other exception | Error with `GenericErrorText`. The log has the exception type only. |

Error text is always encoded (`ErrorText_WithMarkup_IsEncoded`).

The current user is read through `ICurrentUserAccessor`. It is scoped per circuit and caches only a successful `GET /api/me`. Concurrent first calls share one request. A failed call is not cached, so the next call asks the API again. `Invalidate()` clears the cache. The cache is keyed on the `oid` claim: when the signed-in user changes, the cache and any pending request are dropped.

## Static and interactive pages

- `App.razor` picks `InteractiveServer(prerender: false)` only when the request is authenticated and the page accepts interactive routing. Anonymous requests get static server rendering, so no circuit exists before sign-in.
- Pages marked `[ExcludeFromInteractiveRouting]` are static even for signed-in users: the local sign-in picker, signed-out, sign-in error, access denied, not found and error.
- Static pages use dark colours only. The reason is that `app.css` holds no colour tokens for a light scheme, and the theme rules forbid colour literals. CSS media queries would not need JavaScript, so that is not the limit.
- The five static pages in `Pages/` (AccessDenied, Error, NotFound, SignInError, SignedOut) are meant to use only `MudPaper`, `MudText` and `MudIcon`. No test enforces this now. The local picker in the Web also uses `MudGrid` and `MudItem`.
- The picker is a static page with one POST form per account. Each form has an antiforgery token. The sign-out form also has one.
- The providers (`MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`) live once in `MainLayout`. In static pages they render but do nothing interactive.

## Content Security Policy

The Web host sends this policy on every page. The identity provider origin comes from `AzureAd:Instance`:

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' https://login.microsoftonline.com; upgrade-insecure-requests
```

The API sends `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`.

Why the Web policy is this:

- **`style-src 'self' 'unsafe-inline'`.** MudBlazor 9.11.0 sets inline `style` attributes at runtime and has no nonce support, so hashes do not work. `'unsafe-inline'` appears only in `style-src`. Narrower forms fail (table below). CSS injection is the residual risk. Exfiltration through CSS stays closed, because `default-src`, `img-src`, `font-src` and `connect-src` remain `'self'`.
- **`img-src 'self' data:`.** The MudBlazor stylesheet contains a `data:image/png` URL, so `data:` must stay. The earlier plan item to drop it was wrong.
- **Script policy is unchanged.** No inline script, no `on*` attributes and no `javascript:` URLs.

Evidence for the style decision. These were measured during planning in headless Chrome, on same-origin https, with a page that used a button, text field, select, menu, tooltip, table, data grid, tabs, dialog, snackbar, progress and skeleton. The probe projects are not in the repository.

| Style policy | Result |
|---|---|
| `style-src 'self'` | 98 violations; the Mud theme variables are empty |
| Only `style-src-elem` narrowed | Palette empty, app bar transparent, drawer 83 px instead of 240 px |
| `style-src-attr 'none'` | 40 violations; the interactive page stops responding |
| `style-src 'self' 'unsafe-inline'` | 0 violations; everything works |

The source rules in `ThemeSourceScanTests` apply to our markup under `UI.Shared` and `Web`. They reject `style=` attributes (including a `Style` parameter), `<style>` elements, inline `<script>`, `on*` handlers on HTML elements, `javascript:` URLs, external origins in `src`, `href`, `action` and `formaction`, and resource hints (`preconnect`, `preload` and similar).

The browser check is in the E2E project. `EveryVisitedPage_ProducesZeroCspViolationsAndNoPageErrors` (in `ShellSafetyTests`) visits the main screens as an admin and fails on any CSP violation or page error. See [TESTING.md](TESTING.md#browser-e2e-tests).

## Icons

Icons are MudBlazor Material icons, named once in `SvIcons` (`Components/Icons/SvIcons.cs`). The components use those names, and the wrappers pass them to MudBlazor.

## Motion, print and forced colours

- `prefers-reduced-motion: reduce` shortens every animation and transition, and sets the iteration count to 1. The rule uses `!important` so it beats the animations.
- The only infinite animations are the busy spinner and the Web reconnect modal.
- The vault door sealing animation plays once when its `Sealing` parameter is set.
- Forced colours and print rules are in `app.css`. Print hides revealed secret values.

## Ids

Every interactive element and every landmark has a stable kebab-case id, and the wrappers check it. Examples from the shell and pages: `skip-to-content`, `main-content`, `side-nav`, `nav-admin-users`, `account-menu`, `sign-out-button`, `admin-users-table`, `blazor-error-reload`.

## Idle lock

The idle lock runs a small JavaScript module (`wwwroot/js/idle-monitor.js`). When the user has been idle for `IdleLock:Timeout` (default 15 minutes, allowed 1 minute to 8 hours), the module submits the sign-out form. This is configuration only (ADM-4). There is no UI to change it.

- Signed-in static pages load `wwwroot/js/idle-boot.js` from `App.razor`. The page starts the module with the sign-out form id and the timeout, so the timer does not need a circuit.
- Interactive pages start the same module from the `IdleLock` component through JS interop, so they need a live circuit.
- Activity in any tab is shared through `localStorage`. If a submit does not complete, the module tries again after 10 seconds.
- The module also sends a heartbeat to the server at most once a minute while the user is active, so the server's idle limit (the timeout plus 2 minutes of no inbound activity) does not end an active user. A session ended by the server is revoked through the API, so it ends on every instance.
- If the module script is blocked or fails to load, the browser timer does not run. `IdleLock` logs a warning and the page keeps working. On interactive pages the server limit still ends the circuit. Static signed-in pages have no circuit, so they rely on the browser timer, and they hold no vault data. The E2E test `IdleMonitorBlocked_AppKeepsWorkingWithNoErrorBanner` checks that the page keeps working.

## Adding styles

1. Use the MudBlazor palette and components. Do not add a colour literal, a `style` attribute or a `<style>` element.
2. Add shared classes to `app.css`. Keep them small. No test checks the size of the file now.
3. Use existing wrappers before writing markup.
4. Run `dotnet test SharpVault.slnx --filter "FullyQualifiedName~UiShared"` after any change.

## Tests

| Test class | What it checks |
|---|---|
| `ThemeSourceScanTests` | The CSP and Razor source rules above (no inline script or style, no external origins, no resource hints), the disconnected-state rule that hides sensitive values, and the print rule that hides revealed values |
| `ThemeMarkupTests` | Rendered markup of the theme (security-relevant markup only) |
| `ComponentIdsTests` | Id rules |
| `ComponentSourceRulesTests` | Source rules: no direct icon names, and the page and layout rules that remain |
| `IdleMonitorScriptTests`, `ReconnectModalScriptTests` | `idle-monitor.js` and the reconnect script, run under node with a harness |
| Component tests in `UiShared/Components/` | One test class per wrapper: forwarding, encoding and behaviour, rendered with bUnit and `MudTestContext`. Appearance is not asserted |
| Layout and page tests in `UiShared/Layout/` and `UiShared/Pages/` | `ShellTestContext` renders the shell and pages |
| `ThemeWebTests` | Served theme assets, the CSP and the security headers of the Web |
| `WebPagePerformanceTests` | The Web page response budgets (fifty requests per page, 150 ms each) |

The appearance tests (palette, WCAG contrast, font and size budgets, reduced motion, forced colours, focus outline, icon and class-name checks) were removed in the test cleanup. The site tour (`SiteTour`) is how the screens are reviewed now.

`MudTestContext` adds `Services.AddMudServices()` and sets `JSRuntimeMode.Loose`. `ShellTestContext` extends it for shell and page tests.

## MAUI notes

The planned MAUI app (`feature/maui-app`) must:

- link `_content/SharpVault.UI.Shared/css/app.css` and `_content/MudBlazor/MudBlazor.min.css` from its `wwwroot/index.html`, and the MudBlazor script;
- set `<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover" />` and `<meta name="color-scheme" content="dark light" />`;
- set the iOS and Mac Catalyst minimum to 15.4, because the stylesheet uses `:focus-visible` and `dvh`.

## Not done yet

- Browser checks for the skip link and dialog focus. The E2E project has the CSP and page-error check and the idle checks, but no skip-link or dialog-focus journey yet.
- The MAUI `index.html` and the iOS minimum, which come with `feature/maui-app`.
- Pages still use a few MudBlazor primitives directly (`MudText`, `MudPaper`, `MudTh` and `MudTd`). The rule is that pages use the wrappers. Moving them is open work.
