---
name: sharpvault-blazor-ui
description: SharpVault UI conventions for Blazor and .NET MAUI Blazor Hybrid with a shared Razor Class Library. Use when building or changing any page, component, layout or theme in SharpVault.UI.Shared, SharpVault.Web or SharpVault.Maui. Use this instead of blazor-maui-frontend-design in this repository.
---

# SharpVault adaptations (these override the general guide below)

The general Blazor and MAUI guide below is how we work. In SharpVault, these differences apply:

- **Projects:** shared components live in `SharpVault.UI.Shared`, the web host is `SharpVault.Web`, the mobile and desktop host is `SharpVault.Maui`.
- **Every interactive element has a stable `id` attribute** so Playwright Page Objects can select it by `#id`.
- **Authorization in the UI** uses `AuthorizeView` with the `SharpVault.Admin` and `SharpVault.User` roles. Hiding a button is never the security boundary: the API enforces every rule.
- **Secret values** are never rendered by default. They appear only after an explicit reveal, and copying uses a clipboard service that clears the clipboard after a timeout.
- **Idle auto-lock:** the shared layout requires re-authentication after a period of inactivity.
- **Platform services** (clipboard, secure storage, biometrics) sit behind interfaces in `SharpVault.UI.Shared` with implementations in the Web and MAUI hosts.
- **Code style:** follow the `sharpvault-coding-standards` skill. The comments in the examples below explain the guide only. Do not write them in real code.

---

This skill guides creation of distinctive, production-grade frontend interfaces for the **.NET Blazor and MAUI Hybrid ecosystem**. All UI code is implemented as Razor components (.razor) with C# code-behind, designed for sharing between Blazor Web Apps and .NET MAUI Hybrid apps via a common Razor Class Library (RCL).

The user provides frontend requirements: a component, page, application, or interface to build. They may include context about the purpose, audience, or technical constraints.

**CRITICAL CONSTRAINT**: NEVER generate React, Vue, Angular, or plain HTML/JS/TS code. ALL UI must be Blazor Razor components (.razor) with C# logic. Use the .NET ecosystem exclusively.

---

## Solution Architecture

Every project follows a **shared-UI architecture** with three layers:

```
Solution/
├── Solution.Shared/              # Razor Class Library (RCL) — THE CORE
│   ├── Components/               # Reusable Razor components
│   │   ├── Common/               # Buttons, Inputs, Cards, Modals, etc.
│   │   └── Domain/               # Feature-specific composed components
│   ├── Pages/                    # Full pages (shared between Blazor & MAUI)
│   ├── Layouts/                  # Shared layouts
│   ├── Services/                 # Interfaces & shared service implementations
│   ├── Models/                   # DTOs, ViewModels, shared state
│   ├── wwwroot/                  # Static assets (CSS, fonts, images, JS interop)
│   │   ├── css/
│   │   │   └── app.css           # Main stylesheet with CSS variables
│   │   ├── fonts/
│   │   └── js/
│   │       └── interop.js        # Minimal JS interop when absolutely needed
│   └── _Imports.razor            # Shared @using statements
│
├── Solution.Web/                 # Blazor Web App (Server/WASM/Auto)
│   ├── Program.cs                # DI registration, middleware
│   ├── App.razor                 # Root component
│   └── Pages/                    # Web-only pages (if any)
│
└── Solution.Maui/                # .NET MAUI Hybrid App
    ├── MauiProgram.cs            # DI registration, MAUI config
    ├── MainPage.xaml              # BlazorWebView host
    └── Platforms/                 # Platform-specific code
```

### Key Principles

1. **RCL-First**: Put EVERYTHING sharable in the Razor Class Library. Pages, components, services, models, styles — all in `Solution.Shared`. The Web and MAUI projects should be thin shells.
2. **Platform Abstraction**: Define service interfaces in the RCL. Implement platform-specific versions (e.g., file access, notifications, native APIs) in the Web or MAUI projects and register via DI.
3. **CSS Isolation + Global Theme**: Use Blazor CSS isolation (`.razor.css`) for component-scoped styles. Use a global `app.css` in the RCL's `wwwroot` for CSS custom properties (variables), typography, and theme definitions.
4. **Minimal JS Interop**: Avoid JavaScript unless absolutely necessary (e.g., clipboard, scroll position, third-party charting libs). When needed, place interop scripts in the RCL's `wwwroot/js/` and call via `IJSRuntime`.

---

## Design Thinking

Before coding, understand the context and commit to a BOLD aesthetic direction:
- **Purpose**: What problem does this interface solve? Who uses it?
- **Tone**: Pick a strong aesthetic: brutally minimal, maximalist chaos, retro-futuristic, organic/natural, luxury/refined, playful/toy-like, editorial/magazine, brutalist/raw, art deco/geometric, soft/pastel, industrial/utilitarian, etc. Commit fully.
- **Platform Awareness**: Will this run on mobile (MAUI), desktop (MAUI), or web (Blazor)? Design responsive layouts that feel native on each. Touch targets for mobile, keyboard shortcuts for desktop, etc.
- **Differentiation**: What makes this UNFORGETTABLE? What's the one thing someone will remember?

**CRITICAL**: Choose a clear conceptual direction and execute it with precision. Bold maximalism and refined minimalism both work — the key is intentionality, not intensity.

Then implement working Razor components that are:
- Production-grade and functional
- Visually striking and memorable
- Cohesive with a clear aesthetic point-of-view
- Meticulously refined in every detail
- Properly structured for sharing between Blazor Web and MAUI Hybrid

---

## Frontend Aesthetics Guidelines

Focus on:
- **Typography**: Choose fonts that are beautiful, unique, and interesting. Import via Google Fonts or bundle in the RCL's `wwwroot/fonts/`. Avoid generic fonts like Arial, Inter, Segoe UI. Pair a distinctive display font with a refined body font. Load fonts in the RCL so both Blazor and MAUI apps share them.
- **Color & Theme**: Commit to a cohesive aesthetic. Define ALL colors as CSS custom properties in the RCL's `app.css`. Support light/dark themes via `[data-theme="dark"]` or `@media (prefers-color-scheme: dark)`. Dominant colors with sharp accents outperform timid, evenly-distributed palettes.
- **Motion & Transitions**: Use CSS animations and transitions for micro-interactions. For complex orchestration, use CSS `@keyframes` with `animation-delay` for staggered reveals. Leverage Blazor's `@ontransitionend` and `@onanimationend` for state coordination. For advanced scenarios, consider a lightweight CSS animation library loaded in the RCL.
- **Spatial Composition**: Unexpected layouts. Asymmetry. Overlap. Diagonal flow. Grid-breaking elements. Use CSS Grid and Flexbox creatively. Generous negative space OR controlled density.
- **Backgrounds & Visual Details**: Create atmosphere and depth. Apply gradient meshes, noise textures, geometric patterns, layered transparencies, dramatic shadows, decorative borders, and grain overlays — all via CSS in the shared RCL.

NEVER use generic AI-generated aesthetics: overused font families (Inter, Roboto, Arial, Segoe UI, system fonts), cliched color schemes (particularly purple gradients on white backgrounds), predictable Bootstrap/MudBlazor default-styled layouts, and cookie-cutter design that lacks context-specific character.

Interpret creatively and make unexpected choices. No design should be the same. Vary between light and dark themes, different fonts, different aesthetics.

---

## Component Patterns

### Razor Component Structure

Every component follows this pattern:

```razor
@* Components/Common/AppButton.razor *@
@namespace Solution.Shared.Components.Common

<button class="app-button @CssClass @(_isPressed ? "pressed" : "")"
        style="@Style"
        @onclick="HandleClick"
        @attributes="AdditionalAttributes"
        disabled="@Disabled">
    @if (IconLeft is not null)
    {
        <span class="app-button__icon app-button__icon--left">@IconLeft</span>
    }
    <span class="app-button__label">@ChildContent</span>
</button>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? IconLeft { get; set; }
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public string? Style { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private bool _isPressed;

    private async Task HandleClick(MouseEventArgs e)
    {
        if (Disabled) return;
        _isPressed = true;
        await OnClick.InvokeAsync(e);
        _isPressed = false;
    }
}
```

With CSS isolation:

```css
/* Components/Common/AppButton.razor.css */
.app-button {
    font-family: var(--font-body);
    font-weight: 600;
    padding: var(--space-sm) var(--space-lg);
    border: 2px solid var(--color-border);
    background: var(--color-surface);
    color: var(--color-text);
    cursor: pointer;
    transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
    position: relative;
    overflow: hidden;
}

.app-button:hover:not([disabled]) {
    background: var(--color-accent);
    color: var(--color-on-accent);
    transform: translateY(-1px);
    box-shadow: 0 4px 12px var(--color-shadow);
}

.app-button.pressed {
    transform: scale(0.97);
}
```

### Component Hierarchy

Organize components by responsibility:

```
Components/
├── Common/              # Atomic, highly reusable
│   ├── AppButton.razor
│   ├── AppInput.razor
│   ├── AppCard.razor
│   ├── AppModal.razor
│   ├── AppBadge.razor
│   ├── AppAvatar.razor
│   └── AppToast.razor
├── Layout/              # Structural components
│   ├── AppHeader.razor
│   ├── AppSidebar.razor
│   ├── AppFooter.razor
│   └── AppShell.razor   # Main app skeleton
└── Domain/              # Feature-composed components
    ├── Dashboard/
    │   ├── StatsCard.razor
    │   ├── ActivityFeed.razor
    │   └── ChartPanel.razor
    └── Settings/
        ├── ProfileEditor.razor
        └── ThemeSelector.razor
```

---

## Theming System

Define a comprehensive theme in the RCL's global CSS:

```css
/* wwwroot/css/app.css */
:root {
    /* === Typography === */
    --font-display: 'Playfair Display', serif;    /* Example — VARY THIS */
    --font-body: 'DM Sans', sans-serif;            /* Example — VARY THIS */
    --font-mono: 'JetBrains Mono', monospace;

    /* === Color Palette === */
    --color-bg: #0a0a0f;
    --color-surface: #13131a;
    --color-surface-elevated: #1a1a24;
    --color-text: #e8e6e3;
    --color-text-muted: #8a8690;
    --color-accent: #ff6b35;
    --color-accent-glow: rgba(255, 107, 53, 0.15);
    --color-border: #2a2a35;
    --color-shadow: rgba(0, 0, 0, 0.4);
    --color-on-accent: #ffffff;

    /* === Spacing Scale === */
    --space-xs: 0.25rem;
    --space-sm: 0.5rem;
    --space-md: 1rem;
    --space-lg: 1.5rem;
    --space-xl: 2.5rem;
    --space-2xl: 4rem;

    /* === Radii === */
    --radius-sm: 4px;
    --radius-md: 8px;
    --radius-lg: 16px;
    --radius-full: 9999px;

    /* === Transitions === */
    --ease-out: cubic-bezier(0.0, 0.0, 0.2, 1);
    --ease-in-out: cubic-bezier(0.4, 0, 0.2, 1);
    --duration-fast: 150ms;
    --duration-normal: 250ms;
    --duration-slow: 400ms;
}

/* === Light theme override === */
[data-theme="light"] {
    --color-bg: #faf9f7;
    --color-surface: #ffffff;
    --color-surface-elevated: #f5f3f0;
    --color-text: #1a1a1f;
    --color-text-muted: #6b6670;
    --color-border: #e2dfd9;
    --color-shadow: rgba(0, 0, 0, 0.08);
}

/* === Global resets & base === */
*, *::before, *::after {
    box-sizing: border-box;
    margin: 0;
    padding: 0;
}

body {
    font-family: var(--font-body);
    background: var(--color-bg);
    color: var(--color-text);
    line-height: 1.6;
    -webkit-font-smoothing: antialiased;
}

/* === Utility animations === */
@keyframes fadeInUp {
    from { opacity: 0; transform: translateY(16px); }
    to   { opacity: 1; transform: translateY(0); }
}

@keyframes fadeIn {
    from { opacity: 0; }
    to   { opacity: 1; }
}

.animate-in {
    animation: fadeInUp var(--duration-slow) var(--ease-out) both;
}
```

**CRITICAL**: These are EXAMPLES. Every project should have a UNIQUE theme. Change the fonts, colors, spacing philosophy, and overall aesthetic. Never repeat the same theme across projects.

---

## State Management Patterns

Use these patterns in the shared RCL so state works identically in Blazor Web and MAUI Hybrid:

### Cascading State (Simple)

```razor
@* AppStateProvider.razor — wraps the app in a cascading value *@
<CascadingValue Value="this">
    @ChildContent
</CascadingValue>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }

    public string? CurrentTheme { get; private set; } = "dark";
    public UserProfile? User { get; private set; }

    public event Action? OnStateChanged;

    public void SetTheme(string theme)
    {
        CurrentTheme = theme;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnStateChanged?.Invoke();
        InvokeAsync(StateHasChanged);
    }
}
```

### Scoped Service State (Complex)

```csharp
// Services/AppState.cs
public class AppState
{
    public event Action? OnChange;

    private bool _isDarkMode = true;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set { _isDarkMode = value; NotifyStateChanged(); }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}

// Register as scoped in both Web and MAUI DI:
// builder.Services.AddScoped<AppState>();
```

### Platform Services via Interfaces

```csharp
// Services/IDeviceService.cs (in RCL)
public interface IDeviceService
{
    bool IsMobile { get; }
    string Platform { get; }   // "Web", "Android", "iOS", "Windows", "macOS"
    Task<string?> PickFileAsync(string[] allowedTypes);
    Task ShareAsync(string title, string text, string? uri = null);
}

// Implement in Solution.Web with JS interop for browser APIs
// Implement in Solution.Maui with native MAUI APIs (FilePicker, Share, etc.)
```

---

## Platform-Specific Integration

### Blazor Web App (Solution.Web)

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()     // or .AddInteractiveWebAssemblyComponents()
    .AddInteractiveAutoComponents();       // or both for Auto mode

// Register shared services
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<IDeviceService, WebDeviceService>();

var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddInteractiveWebAssemblyRenderMode()
   .AddAdditionalAssemblies(typeof(Solution.Shared._Imports).Assembly);

app.Run();
```

### .NET MAUI Hybrid (Solution.Maui)

```csharp
// MauiProgram.cs
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // Bundle the same fonts used in the RCL
                fonts.AddFont("DMSans-Regular.ttf", "DMSans");
                fonts.AddFont("PlayfairDisplay-Bold.ttf", "PlayfairDisplay");
            });

        builder.Services.AddMauiBlazorWebView();
        #if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        #endif

        // Register shared services
        builder.Services.AddScoped<AppState>();
        builder.Services.AddScoped<IDeviceService, MauiDeviceService>();

        return builder.Build();
    }
}
```

```xml
<!-- MainPage.xaml -->
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="Solution.Maui.MainPage">
    <BlazorWebView HostPage="wwwroot/index.html">
        <BlazorWebView.RootComponents>
            <RootComponent Selector="#app"
                           ComponentType="{x:Type shared:Routes}"
                           x:Name="rootComponent" />
        </BlazorWebView.RootComponents>
    </BlazorWebView>
</ContentPage>
```

---

## Responsive Design for Dual Targets

Since the same Razor components run on web browsers (all sizes) AND in a MAUI WebView (phone, tablet, desktop), design responsively:

```css
/* In component .razor.css or global app.css */

/* Mobile-first base styles */
.dashboard-grid {
    display: grid;
    grid-template-columns: 1fr;
    gap: var(--space-md);
    padding: var(--space-md);
}

/* Tablet */
@media (min-width: 768px) {
    .dashboard-grid {
        grid-template-columns: repeat(2, 1fr);
        gap: var(--space-lg);
        padding: var(--space-lg);
    }
}

/* Desktop */
@media (min-width: 1024px) {
    .dashboard-grid {
        grid-template-columns: repeat(3, 1fr);
        gap: var(--space-xl);
        padding: var(--space-xl);
    }
}

/* Large desktop */
@media (min-width: 1440px) {
    .dashboard-grid {
        grid-template-columns: repeat(4, 1fr);
        max-width: 1400px;
        margin: 0 auto;
    }
}
```

Use the `IDeviceService` to conditionally render platform-specific UI when CSS alone isn't enough:

```razor
@inject IDeviceService Device

@if (Device.IsMobile)
{
    <MobileNavDrawer />
}
else
{
    <DesktopSidebar />
}
```

---

## UI Component Libraries (Optional)

When the user wants to use a third-party component library, prefer these .NET-native options (in order of recommendation):

1. **MudBlazor** — Rich, Material-inspired components. Good defaults, highly customizable. Always override default MudBlazor theme to match the project aesthetic — NEVER ship with default MudBlazor purple.
2. **Radzen Blazor** — Large component set, good for data-heavy apps (grids, charts, schedulers).
3. **Fluent UI Blazor** — Microsoft's Fluent design system. Good for enterprise/productivity apps.
4. **Syncfusion Blazor** — Enterprise-grade with 80+ components (paid, but free community license).

When using any library:
- **Always customize the theme** to match the project's unique aesthetic.
- **Wrap library components** in your own RCL components for consistency and future flexibility.
- **Don't let the library dictate the design** — it provides building blocks, not the vision.

---

## JS Interop (When Unavoidable)

Keep JS interop minimal and centralized in the RCL:

```javascript
// wwwroot/js/interop.js
export function scrollToElement(elementId) {
    document.getElementById(elementId)?.scrollIntoView({ behavior: 'smooth' });
}

export function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
}

export function getViewportSize() {
    return { width: window.innerWidth, height: window.innerHeight };
}
```

```csharp
// Services/JsInteropService.cs
public class JsInteropService : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

    public JsInteropService(IJSRuntime jsRuntime)
    {
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./_content/Solution.Shared/js/interop.js").AsTask());
    }

    public async Task ScrollToElementAsync(string elementId)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("scrollToElement", elementId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
```

---

## Checklist Before Generating Code

When generating any frontend code for this user, always verify:

- [ ] All UI is Razor components (.razor) with C# — NO React, Vue, or plain JS
- [ ] Shared components/pages live in the RCL project, not in Web or MAUI projects
- [ ] CSS custom properties define the theme in a global `app.css`
- [ ] CSS isolation (`.razor.css`) is used for component-scoped styles
- [ ] The aesthetic is distinctive and unique — not default Bootstrap/MudBlazor/generic
- [ ] Platform-specific behavior uses DI interfaces, not `#if` preprocessor hacks
- [ ] Responsive design covers mobile through desktop
- [ ] Font choices are distinctive and loaded properly for both Web and MAUI
- [ ] Animations use CSS transitions/keyframes (not JS)
- [ ] Components use `[Parameter]`, `EventCallback`, `RenderFragment` correctly

---

## Important Reminders

**Match implementation complexity to the aesthetic vision.** Maximalist designs need elaborate CSS with extensive animations. Minimalist designs need restraint, precision, and careful attention to spacing and typography.

**IMPORTANT**: Match implementation complexity to the aesthetic vision. Maximalist designs need elaborate code with extensive animations and effects. Minimalist or refined designs need restraint, precision, and careful attention to spacing, typography, and subtle details. Elegance comes from executing the vision well.

Remember: Claude is capable of extraordinary creative work. Don't hold back — show what can truly be created when thinking outside the box and committing fully to a distinctive vision, all within the Blazor/.NET ecosystem.
