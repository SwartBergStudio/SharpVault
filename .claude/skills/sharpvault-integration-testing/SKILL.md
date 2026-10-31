---
name: sharpvault-integration-testing
description: SharpVault testing conventions. Use when writing, updating or debugging unit tests, API integration tests (WebApplicationFactory, Testcontainers Azurite, AutoFixture, xUnit) or Playwright end-to-end tests with the Blazor Page Object Model. Use this instead of dotnet-integration-testing in this repository.
---

# SharpVault adaptations (these override the general guide below)

The general testing guide below is how we work. In SharpVault, these differences apply:

- **Azurite instead of SQL Server.** `ApiTestFactory` starts an Azurite container with `Testcontainers.Azurite` and points the API's Table Storage configuration at its connection string. There is no `DbContext` in tests.
- **No Respawn.** Reset state after each test by deleting and recreating the tables (or deleting all entities) in `DisposeAsync`. Expose this as `factory.ResetStorage()` and keep the same lifecycle rule: reset after every test.
- **Data helpers use `TableClient`.** `Insert<T>`, `InsertMany<T>` and `FindById<T>` in `BaseIntegrationTest` go through the store interfaces or `TableClient`, not EF Core.
- **No login endpoint.** Authentication is Microsoft Entra ID, which tests cannot call. Register a test authentication handler in `ApiTestFactory` that issues claims for `oid`, `name` and the `SharpVault.Admin` or `SharpVault.User` role. Provide helpers `SetAdminAuthenticatedClient()`, `SetUserAuthenticatedClient(Guid oid)` and `CreateUnauthenticatedClient()`.
- **Development key provider.** Tests use the development `IKeyWrapProvider`, never Azure Key Vault.
- **Security tests are required.** For every endpoint, cover: a user cannot read or change another user's secrets, a user cannot reach admin endpoints, and unauthenticated calls return 401.
- **Admin transfer tests** must cover a full transfer, a transfer that is interrupted and resumed (no lost or duplicated secrets), and the audit entries it writes.
- **Unit tests** live in `SharpVault.UnitTests` with the same conventions (xUnit, AutoFixture). Crypto unit tests must cover round-trip, tampering with ciphertext, nonce or associated data, and nonce uniqueness.
- **Test naming:** `MethodOrScenario_Condition_ExpectedResult`.
- **Code style:** follow the `sharpvault-coding-standards` skill. The comments in the examples below explain the guide only. Do not write them in real code. Use descriptive test names instead of comments, and an XML doc comment only when overriding a base test to opt out.

---

This skill guides the creation and maintenance of integration tests for .NET Clean Architecture applications. It covers two distinct test types that share common infrastructure:

1. **API Integration Tests** — HTTP-level tests against minimal API endpoints using real database (Testcontainers) with Respawn reset
2. **UI End-to-End Tests** — Playwright-based browser tests against a running Blazor app with Page Object Model

**CRITICAL**: Follow every convention in this skill precisely. These patterns exist to make tests reliable, maintainable, and accessible to junior developers.

---

## Test Project Structure

```
Tests/
├── IntegrationTests/
│   ├── Factories/                        # Test infrastructure
│   │   ├── ApiTestFactory.cs             # WebApplicationFactory + Testcontainers + Kestrel
│   │   ├── WebTestFactory.cs             # Blazor web app factory for E2E
│   │   ├── DatabaseTestCollection.cs     # xUnit collection for API tests (shared DB)
│   │   ├── UITestCollection.cs           # xUnit collection(s) for E2E tests
│   │   └── UITestCollection2.cs          # Additional collections for parallel isolation
│   │
│   ├── APITests/                         # API integration tests
│   │   ├── BaseIntegrationTest.cs        # Shared test base (HttpClient, DB, auth, helpers)
│   │   ├── BaseCrudControllerTests.cs    # Generic CRUD test base (virtual test methods)
│   │   ├── AddressControllerTests.cs     # Concrete entity tests
│   │   ├── CategoryControllerTests.cs
│   │   └── [Feature]ControllerTests.cs
│   │
│   ├── UITests/                          # E2E browser tests
│   │   ├── EndToEndTestBase.cs           # Playwright + browser + Blazor-aware infrastructure
│   │   ├── UserTestBase.cs               # User-facing helpers (login, navigation, assertions)
│   │   ├── UserJourneyTest.cs            # Base for journey-style E2E tests
│   │   ├── EmailTestBase.cs              # Email retrieval + token extraction helpers
│   │   └── Journeys/                     # Actual E2E test scenarios
│   │       ├── ForgotPasswordJourney.cs
│   │       ├── AdminCategoryManagementJourney.cs
│   │       ├── UserRegistrationJourney.cs
│   │       └── [Feature]Journey.cs
│   │
│   ├── Pages/                            # Page Object Model classes
│   │   ├── BasePage.cs                   # Shared page helpers (alerts, Blazor waits)
│   │   ├── LoginPage.cs
│   │   ├── ForgotPasswordPage.cs
│   │   ├── ResetPasswordPage.cs
│   │   ├── NavMenuPage.cs
│   │   ├── CategoryIndexPage.cs
│   │   ├── CategoryCrudPage.cs
│   │   └── [Feature]Page.cs
│   │
│   └── appsettings.json                  # Playwright config (headless, slowmo, etc.)
```

---

## Test Infrastructure — Factories

### ApiTestFactory

The `ApiTestFactory` provides:
- A real API server running on Kestrel (not just in-memory `TestServer`)
- A real database via **Testcontainers** (SQL Server in Docker)
- **Respawn** for fast database cleanup between tests
- An `HttpClient` preconfigured with the API base URL

```csharp
// Factories/ApiTestFactory.cs — Key responsibilities:

public class ApiTestFactory : IAsyncLifetime
{
    // Testcontainers: spins up a real SQL Server container
    // WebApplicationFactory<Program>: hosts the API on a real Kestrel port
    // Respawn: Checkpoint for fast DB reset between tests

    public DatabaseContext Db { get; }       // Shared DbContext for test setup/assertions
    public string ApiUrl { get; }            // e.g., "https://localhost:5001/"

    public HttpClient CreateApiClient();     // Preconfigured HttpClient
    public Task ResetDatabase();             // Respawn checkpoint reset
    public void StartApiServer();            // Start Kestrel on real port
}
```

### WebTestFactory

The `WebTestFactory` provides:
- A real Blazor web app server running on Kestrel
- Configured to point at the `ApiTestFactory`'s API URL

```csharp
// Factories/WebTestFactory.cs — Key responsibilities:

public class WebTestFactory : IAsyncLifetime
{
    public void SetApiUrl(string apiUrl);    // Point Blazor app at API
    public void StartWebServer();            // Start Blazor on real port
    public string GetServerUrl();            // e.g., "https://localhost:5002/"
}
```

### xUnit Collections

Tests that share database infrastructure use xUnit collection fixtures for lifecycle management:

```csharp
// API tests share a single database container
[CollectionDefinition(nameof(DatabaseTestCollection))]
public class DatabaseTestCollection : ICollectionFixture<ApiTestFactory>;

// UI tests get their own collections for parallel isolation
[CollectionDefinition(nameof(UITestCollection))]
public class UITestCollection : ICollectionFixture<ApiTestFactory>, ICollectionFixture<WebTestFactory>;

// Additional UI collections for tests that must not share state
[CollectionDefinition(nameof(UITestCollection2))]
public class UITestCollection2 : ICollectionFixture<ApiTestFactory>, ICollectionFixture<WebTestFactory>;

[CollectionDefinition(nameof(UITestCollection3))]
public class UITestCollection3 : ICollectionFixture<ApiTestFactory>, ICollectionFixture<WebTestFactory>;
```

**CRITICAL**: Every test class MUST have a `[Collection(...)]` attribute. Tests without a collection will create their own factory instances, causing port conflicts and container sprawl.

---

## API Integration Tests

### BaseIntegrationTest

The foundation for all API tests. Provides HttpClient, DbContext access, authentication helpers, AutoFixture, and data manipulation methods:

```csharp
// APITests/BaseIntegrationTest.cs

public class BaseIntegrationTest : IAsyncLifetime
{
    protected HttpClient Client;
    protected DatabaseContext DbContext => _factory.Db;
    protected string ApiUrl => _factory.ApiUrl;
    internal readonly Fixture _fixture = new();
    private readonly Func<Task> _resetDatabase;
    private readonly ApiTestFactory _factory;

    public BaseIntegrationTest(ApiTestFactory factory)
    {
        _resetDatabase = factory.ResetDatabase;
        Client = factory.CreateApiClient();
        Client.DefaultRequestHeaders.Add("X-API-KEY", "your-api-key-here");
        _factory = factory;

        // Global AutoFixture customizations for value objects
        _fixture.Customize<Money>(c => c
            .FromFactory((decimal amount) => new Money(amount, "USD")));
    }

    // --- Data helpers ---

    public async Task Insert<T>(T entity) where T : class
    {
        await DbContext.AddAsync(entity);
        await DbContext.SaveChangesAsync();
    }

    public async Task InsertMany<T>(IEnumerable<T> entities) where T : class
    {
        await DbContext.AddRangeAsync(entities);
        await DbContext.SaveChangesAsync();
    }

    public async Task<T?> FindById<T>(Guid id) where T : class
    {
        // Use a new scope to avoid stale DbContext tracking issues
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        return await dbContext.Set<T>().FindAsync(id);
    }

    // --- Lifecycle ---

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _resetDatabase();  // Respawn resets DB after each test

    // --- Authentication ---

    public async Task SetAdministratorAuthenticatedClient()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    protected HttpClient CreateUnauthenticatedClient()
    {
        return _factory.CreateApiClient();
    }

    private string _adminToken = default!;

    private async Task<string> GetAdminTokenAsync()
    {
        if (!string.IsNullOrEmpty(_adminToken))
            return _adminToken;

        var loginDto = new LoginDto
        {
            Username = ApplicationUser.DefaultAdministratorUsername,
            Password = ApplicationUser.DefaultAdministratorPassword
        };

        var content = new StringContent(
            JsonSerializer.Serialize(loginDto), Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("Account/login", content);

        if (!response.IsSuccessStatusCode)
            throw new Exception("Unable to authenticate as admin user");

        var authResult = await response.Content.ReadFromJsonAsync<Result<AuthResultDto>>();
        _adminToken = authResult!.Data!.AccessToken
            ?? throw new Exception("AccessToken was null in admin login response");

        return _adminToken;
    }

    public async Task<string> GetAdminUserIdAsync()
    {
        var token = await GetAdminTokenAsync();
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);
        return jwtToken.Claims.First(claim =>
            claim.Type == ClaimTypes.NameIdentifier).Value;
    }

    // --- Email helpers (for tests that verify email side effects) ---

    protected async Task<EmailHistory?> WaitForEmailAsync(
        string to, string template, int timeoutMs = 15000, int pollIntervalMs = 100)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        EmailHistory? email = null;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            email = await DbContext.EmailHistories
                .Where(e => e.To == to && e.Template == template)
                .OrderByDescending(e => e.CreatedDate)
                .FirstOrDefaultAsync();
            if (email != null && email.DidSend)
                return email;
            await Task.Delay(pollIntervalMs);
        }
        return email;
    }
}
```

### Key Conventions for BaseIntegrationTest

- **`DisposeAsync` calls `_resetDatabase()`** — Respawn runs after every test, not before. This keeps the DB clean for the next test.
- **`FindById` uses a new DI scope** — avoids stale entity tracking from the shared `DbContext`.
- **Admin token is cached** — `GetAdminTokenAsync()` only authenticates once per test class instance.
- **AutoFixture `_fixture`** is `internal` so derived test classes can customize and use it directly.

---

### BaseCrudControllerTests<TEntity, TDto>

A generic base that provides standard CRUD test methods as `virtual` — concrete tests inherit and override as needed:

```csharp
// APITests/BaseCrudControllerTests.cs

public abstract class BaseCrudControllerTests<TEntity, TDto>
    : BaseIntegrationTest
    where TEntity : BaseEntity
    where TDto : BaseEntityDto
{
    public BaseCrudControllerTests(ApiTestFactory factory) : base(factory) { }

    // --- Required overrides ---
    public abstract string EndpointControllerName { get; }
    internal abstract Task SetAuthenticationForClient();
    protected abstract byte[]? CreateTestExcelFile();
    protected abstract Task<TEntity?> CreateExistingEntityForImportAsync();
    protected abstract Task ValidateImportedDataAsync(TEntity? existingEntity);

    // --- Virtual CRUD tests (override to customize or skip) ---

    [Fact]
    public virtual async Task CreateEntity_ShouldReturnCreatedEntity() { ... }

    [Fact]
    public virtual async Task UpdateEntity_ShouldReturnUpdatedEntity() { ... }

    [Fact]
    public virtual async Task DeleteEntity_ShouldReturnSuccess() { ... }

    [Fact]
    public virtual async Task GetEntityById_ShouldReturnEntity() { ... }

    [Fact]
    public virtual async Task GetPaginatedData_ShouldReturnPaginatedResults() { ... }

    [Fact]
    public virtual async Task ExportData_ShouldReturnExportedData() { ... }

    [Fact]
    public virtual async Task ExportPDF_ShouldReturnExportedPDF() { ... }

    [Fact]
    public virtual async Task ImportData_ShouldReturnSuccess() { ... }
}
```

### Writing Concrete API Tests

Each entity gets its own test class. The pattern:

1. Inherit `BaseCrudControllerTests<TEntity, TDto>`
2. Set `EndpointControllerName`
3. Implement `SetAuthenticationForClient()`
4. Customize AutoFixture for the entity and DTO in the constructor
5. Override base tests that don't apply (return `Assert.True(true)` with a doc comment explaining why)
6. Add feature-specific `[Fact]` tests

```csharp
// APITests/AddressControllerTests.cs

[Collection(nameof(DatabaseTestCollection))]
public class AddressControllerTests : BaseCrudControllerTests<Address, AddressDto>
{
    public AddressControllerTests(ApiTestFactory factory) : base(factory)
    {
        // AutoFixture customization — handle navigation properties and required fields
        _fixture.Customize<Address>(c => c
            .Without(i => i.User)                    // Skip nav properties
            .With(i => i.UserId, () => GetAdminUserIdAsync().Result)
            .With(i => i.Line1, () => $"Line1_{Guid.NewGuid().ToString()[..8]}")
            .With(i => i.City, () => $"City_{Guid.NewGuid().ToString()[..8]}")
            .With(i => i.PostalCode, () => Guid.NewGuid().ToString()[..5])
            .With(i => i.CountryCode, () => "ZAR")
            .With(i => i.Country, () => "Country")
            .With(i => i.Coordinates, () => new GeoCoordinate(-25.7585, 28.1919)));

        _fixture.Customize<AddressDto>(c => c
            .With(i => i.UserId, () => GetAdminUserIdAsync().Result)
            .With(i => i.Line1, () => $"Line1_{Guid.NewGuid().ToString()[..8]}")
            .With(i => i.City, () => $"City_{Guid.NewGuid().ToString()[..8]}")
            .With(i => i.PostalCode, () => Guid.NewGuid().ToString()[..5])
            .With(i => i.CountryCode, () => "ZAR")
            .With(i => i.Country, () => "Country")
            .With(i => i.Latitude, () => -25.7585)
            .With(i => i.Longitude, () => 28.1919));
    }

    public override string EndpointControllerName => "Address";

    internal override async Task SetAuthenticationForClient()
    {
        await SetAdministratorAuthenticatedClient();
    }

    // --- Opt out of tests that don't apply ---

    /// <summary>
    /// Addresses are user-specific. Import is not supported.
    /// </summary>
    protected override byte[]? CreateTestExcelFile() => null;

    /// <summary>
    /// Addresses are user-specific. Import is not supported.
    /// </summary>
    protected override async Task<Address?> CreateExistingEntityForImportAsync() => null;

    /// <summary>
    /// Addresses are user-specific. Import is not supported.
    /// </summary>
    protected override Task ValidateImportedDataAsync(Address? existingEntity)
        => Task.CompletedTask;

    /// <summary>
    /// Addresses are user-specific. Bulk import not supported.
    /// </summary>
    public override Task ImportData_ShouldReturnSuccess()
    {
        Assert.True(true);
        return Task.CompletedTask;
    }

    // --- Feature-specific tests ---

    [Fact]
    public async Task GetMyAddresses_ShouldReturnUserAddresses()
    {
        await SetAuthenticationForClient();
        var userId = await GetAdminUserIdAsync();
        var addresses = _fixture.CreateMany<Address>(5).ToList();
        addresses.ForEach(a => a.UserId = userId);
        await InsertMany(addresses);

        var response = await Client.GetAsync($"{EndpointControllerName}/GetMyAddresses");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Result<List<AddressDto>>>();

        Assert.True(result?.Succeeded, "Http client call failed");
        Assert.NotNull(result?.Data);
        Assert.True(5 <= result?.Data?.Count);
    }
}
```

### AutoFixture Customization Rules

When customizing AutoFixture for entities:

- **Always use `.Without()` for navigation properties** — AutoFixture cannot create EF navigation objects, and they'll cause insert failures.
- **Use `.With(prop, () => value)` for required fields** — ensures valid data that satisfies DB constraints.
- **Generate unique values** with `Guid.NewGuid().ToString()[..8]` for string fields that have unique indexes.
- **Customize both the Entity AND the DTO** — they need matching valid data.
- **Handle value objects explicitly** — like `GeoCoordinate`, `Money`, etc.

### Opting Out of Base Tests

When a base test doesn't apply to an entity, override it with `Assert.True(true)` and a `<summary>` doc comment explaining WHY:

```csharp
/// <summary>
/// Addresses are user-specific entities.
/// Bulk import is not supported for addresses.
/// </summary>
public override Task ImportData_ShouldReturnSuccess()
{
    Assert.True(true);
    return Task.CompletedTask;
}
```

**CRITICAL**: Always include the doc comment. Without it, future developers won't know if the test was intentionally skipped or accidentally broken.

---

## UI End-to-End Tests

### Test Class Hierarchy

```
EndToEndTestBase                    # Playwright + browser + Blazor infrastructure
├── UserTestBase                    # User-facing helpers (login state, navigation, messages)
│   └── UserJourneyTest            # Base for journey-style E2E tests
│       └── AdminCategoryJourney   # Concrete journey test
├── EmailTestBase                   # Email retrieval + token extraction
│   └── ForgotPasswordJourney      # Concrete journey with email verification
```

### EndToEndTestBase

The infrastructure layer for all E2E tests. Handles Playwright lifecycle, browser setup, Blazor-aware navigation, and shared utilities:

```csharp
// UITests/EndToEndTestBase.cs

public abstract class EndToEndTestBase : IAsyncLifetime
{
    protected IPlaywright PlaywrightInstance = default!;
    protected IBrowser Browser = default!;
    protected IBrowserContext BrowserContext = default!;
    protected IPage Page = default!;
    protected IConfiguration Configuration = default!;
    protected string BaseUrl = default!;
    protected WebTestFactory WebFactory { get; }
    protected ApiTestFactory ApiFactory { get; }
    protected string ApiUrl = default!;
    protected DatabaseContext DbContext => ApiFactory.Db;

    private const int DefaultViewportWidth = 1920;
    private const int DefaultViewportHeight = 1080;

    // Constructor wires both factories together
    public EndToEndTestBase(ApiTestFactory apiFactory, WebTestFactory webFactory)
    {
        ApiFactory = apiFactory;
        WebFactory = webFactory;
        WebFactory.SetApiUrl(ApiFactory.ApiUrl);
    }

    public async Task InitializeAsync()
    {
        // 1. Load configuration (headless, slowmo from appsettings)
        // 2. Start API server on real port
        // 3. Start Web server pointing at API
        // 4. Launch Playwright Chromium with consistent viewport
        // 5. Create browser context and page
    }

    public async Task DisposeAsync()
    {
        // Close page → context → browser → Playwright (in order)
    }

    // --- Navigation (Blazor-aware) ---

    protected async Task NavigateToAsync(string path)
    {
        await Page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });
        await WaitForBlazorAsync();  // CRITICAL: Wait for Blazor + MudBlazor JS interop
    }

    protected async Task WaitForBlazorAsync(int timeoutMs = 5000)
    {
        try
        {
            await Page.WaitForFunctionAsync(@"
                () => {
                    if (!window.Blazor) return false;
                    if (typeof window.mudElementRef !== 'undefined') return true;
                    return document.readyState === 'complete';
                }
            ", new PageWaitForFunctionOptions { Timeout = timeoutMs });
            await Task.Delay(500);  // Extra delay for MudBlazor component registration
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Warning: Blazor initialization check timed out");
        }
    }

    // --- Utility methods ---

    protected bool IsOnPage(string pagePath);
    protected string GetCurrentUrl();
    protected async Task ResetDatabaseAsync();
    protected async Task WaitForPageTextAsync(string text, int timeoutMs = 5000);
    protected async Task TakeScreenshotAsync(string testName, string description);
    protected async Task<string> GetUserIdByEmailAsync(string email);
}
```

### Key Conventions for EndToEndTestBase

- **Always call `WaitForBlazorAsync()` after navigation** — MudBlazor components need JS interop to initialize. Without this, element interactions will fail with timing errors.
- **Use `NetworkIdle` wait strategy** — not `DOMContentLoaded`, because Blazor apps load in multiple phases.
- **Viewport is always 1920x1080** — ensures consistent rendering across local and CI environments. Never rely on responsive breakpoints in tests.
- **`TakeScreenshotAsync` for debugging** — call this in catch blocks during development to diagnose failures.

---

### UserTestBase

Adds user-facing helpers on top of `EndToEndTestBase`:

```csharp
// UITests/UserTestBase.cs

public abstract class UserTestBase : EndToEndTestBase
{
    // --- Navigation shortcuts ---
    protected async Task GoToLoginPageAsync();
    protected async Task GoToRegisterPageAsync();
    protected async Task GoToProfilePageAsync();
    // ... etc

    // --- Assertions ---

    protected async Task AssertUserIsLoggedInAsync()
    {
        // Opens nav menu, checks for profile link
        var navMenu = new NavMenuPage(Page);
        await navMenu.OpenMenuAsync();
        var isLoggedIn = await navMenu.IsUserLoggedInAsync();
        if (!isLoggedIn)
            throw new AssertionException("User does not appear to be logged in");
        await navMenu.CloseMenuAsync();
    }

    protected void AssertCurrentPageIs(string expectedPath)
    {
        if (!Page.Url.Contains(expectedPath, StringComparison.OrdinalIgnoreCase))
            throw new AssertionException(
                $"Expected page '{expectedPath}', actual URL: '{Page.Url}'");
    }

    protected async Task AssertSuccessMessageAppearsAsync(string expectedText = "")
    {
        var basePage = new BasePage(Page);
        var alertMessages = basePage.GetAlertsLocator();
        // ... check count > 0, optionally match text
    }

    protected async Task AssertErrorMessageAppearsAsync(string expectedText = "")
    {
        // Same pattern as success
    }

    // --- Interaction helpers ---

    protected async Task ClickByTextAsync(string text)
    {
        await Page.Locator($"button:has-text('{text}'), a:has-text('{text}')").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
```

### EmailTestBase

For E2E tests that verify email side effects (forgot password, registration confirmation, etc.):

```csharp
// UITests/EmailTestBase.cs

public abstract class EmailTestBase : EndToEndTestBase
{
    protected async Task<EmailHistory?> WaitForEmailAsync(
        string recipientEmail, string templateName, int timeoutMs = 15000)
    {
        // Polls DbContext until email record appears with DidSend = true
    }

    protected string ExtractTokenFromEmail(string emailBody)
    {
        // Regex to extract tokens from email URLs
        var match = Regex.Match(emailBody,
            @"/(confirm-email|password-reset)/([^/""']+)/([^/""']+)",
            RegexOptions.IgnoreCase);
        return Uri.UnescapeDataString(match.Groups[3].Value);
    }
}
```

---

## Page Object Model (POM)

**CRITICAL**: Every Blazor page that tests interact with MUST have a corresponding Page Object class. This is the single most important convention for maintainable E2E tests.

### Rules

1. **All selectors use `#id`** — NEVER use CSS class selectors, text-based selectors, or XPath in Page Objects. Every interactable element in the Blazor component MUST have an `id` attribute.
2. **One class per page/dialog** — `LoginPage`, `CategoryIndexPage`, `CategoryCrudPage`, etc.
3. **All Page Objects inherit `BasePage`** — shared helpers for alerts, Blazor waits, etc.
4. **Locators are private properties** — expose behavior via public async methods, not raw locators.
5. **Methods wrap Playwright calls with error handling** — throw descriptive exceptions on failure.
6. **Include `WaitForPageLoadAsync()`** — every page object should have a method to confirm the page is ready.

### BasePage

```csharp
// Pages/BasePage.cs

public class BasePage
{
    protected readonly IPage Page;

    public BasePage(IPage page)
    {
        Page = page;
    }

    public ILocator GetAlertsLocator()
    {
        return Page.Locator(".mud-alert-message");  // MudBlazor alert component
    }

    public async Task WaitForBlazorAsync(int timeoutMs = 5000)
    {
        try
        {
            await Page.WaitForFunctionAsync(@"
                () => {
                    if (!window.Blazor) return false;
                    if (typeof window.mudElementRef !== 'undefined') return true;
                    return document.readyState === 'complete';
                }
            ", new PageWaitForFunctionOptions { Timeout = timeoutMs });
            await Task.Delay(500);
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Warning: Blazor initialization check timed out");
        }
    }

    public async Task WaitForPageLoadAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task ClickAsync(string selector)
    {
        await Page.Locator(selector).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
```

### Concrete Page Object Example

```csharp
// Pages/ForgotPasswordPage.cs

public class ForgotPasswordPage : BasePage
{
    public ForgotPasswordPage(IPage page) : base(page) { }

    // --- Locators: ALWAYS use #id selectors ---
    private ILocator EmailInput => Page.Locator("#email");
    private ILocator ResetPasswordButton => Page.Locator("#resetpassword");
    private ILocator BackToLoginButton => Page.Locator("#backtologin");
    private ILocator BackToLoginConfirmButton => Page.Locator("#backtologinconfirm");
    private ILocator PageHeading => Page.Locator("#forgotpasswordheading");
    private ILocator ConfirmationMessage => Page.Locator("#forgotpasswordconfirmation");

    // --- Navigation ---

    public async Task NavigateAsync(string baseUrl)
    {
        var url = $"{baseUrl}forgot-password";
        try
        {
            await Page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });
            await WaitForBlazorAsync();
            await WaitForPageLoadAsync();
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"Failed to navigate to forgot password page. " +
                $"Expected: {url}, Actual: {Page.Url}. Error: {ex.Message}", ex);
        }
    }

    // --- Page readiness ---

    public new async Task WaitForPageLoadAsync()
    {
        try
        {
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await PageHeading.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"Forgot password page did not load. URL: {Page.Url}. Error: {ex.Message}", ex);
        }
    }

    // --- Interactions (wrap with error context) ---

    public async Task FillEmailAsync(string email)
    {
        try
        {
            await EmailInput.FillAsync(email);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to fill email field. Error: {ex.Message}", ex);
        }
    }

    public async Task SubmitResetAsync()
    {
        try
        {
            await ResetPasswordButton.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to submit reset form. Error: {ex.Message}", ex);
        }
    }

    // --- Composite actions ---

    public async Task RequestPasswordResetAsync(string email, string baseUrl)
    {
        await NavigateAsync(baseUrl);
        await FillEmailAsync(email);
        await SubmitResetAsync();
    }

    // --- State queries ---

    public async Task<bool> IsConfirmationMessageVisibleAsync()
    {
        try { return await ConfirmationMessage.IsVisibleAsync(); }
        catch { return false; }
    }

    public async Task<bool> IsPageLoadedAsync()
    {
        return await PageHeading.IsVisibleAsync();
    }
}
```

### Page Object Conventions

- **Locators are `private` properties** using `=> Page.Locator("#id")` — lazy evaluation, always fresh.
- **Public methods are async Tasks** that perform one logical action.
- **Composite methods** combine multiple steps (e.g., `RequestPasswordResetAsync` = navigate + fill + submit).
- **All interactions wrapped in try/catch** with contextual error messages including current URL.
- **Every page has `WaitForPageLoadAsync()`** — waits for a key element that proves the page rendered.

### Adding IDs to Blazor Components

For every page that needs E2E tests, add `id` attributes to interactable elements:

```razor
@* Pages/ForgotPassword.razor *@

<MudText id="forgotpasswordheading" Typo="Typo.h4">Forgot Password</MudText>

<MudTextField id="email"
              @bind-Value="_model.Email"
              Label="Email"
              Variant="Variant.Outlined" />

<MudButton id="resetpassword"
           OnClick="HandleSubmit"
           Variant="Variant.Filled"
           Color="Color.Primary">
    Reset Password
</MudButton>

<MudButton id="backtologin"
           Href="/login"
           Variant="Variant.Text">
    Back to Login
</MudButton>
```

**CRITICAL**: When creating or modifying Blazor pages, ALWAYS add `id` attributes to elements that tests will interact with. Use descriptive, page-scoped IDs (e.g., `#forgotpasswordheading`, not `#heading`).

---

## Journey Tests (E2E Scenarios)

Journey tests simulate a complete user workflow as a sequential story. They are NOT isolated unit-style tests — they flow through multiple pages.

### Pattern

```csharp
// UITests/Journeys/AdminCategoryManagementJourney.cs

[Collection(nameof(UITestCollection3))]
public class AdminCategoryManagementJourney : UserJourneyTest
{
    private const string AdminUsername = "Administrator";
    private const string AdminPassword = "admin-password-here";

    public AdminCategoryManagementJourney(
        ApiTestFactory apiFactory, WebTestFactory webFactory)
        : base(apiFactory, webFactory) { }

    [Fact]
    public async Task AdminCategoryManagement_SequentialJourney_ShouldSucceed()
    {
        // Generate unique test data to avoid collisions
        var uid = Guid.NewGuid().ToString()[..8];
        var parentName = $"0 Medical {uid}";
        var parentSlug = $"medical-{uid}";
        var childName = $"0 Doctor {uid}";
        var childSlug = $"doctor-{uid}";

        // Page objects
        var indexPage = new CategoryIndexPage(Page);
        var crudPage = new CategoryCrudPage(Page);

        // Step 1: Login as admin
        await LoginAsAdminAsync();

        // Step 2: Navigate to categories
        await NavigateToAsync("categories");
        await indexPage.WaitForDataGridAsync();

        // Step 3: Create parent category
        await indexPage.ClickCreateAsync();
        await crudPage.FillNameAndSlugAsync(parentName, parentSlug);
        await crudPage.ClickAsync("#crudsave");
        await crudPage.WaitForDialogHiddenAsync();

        // Step 4: Verify parent appears in list
        await NavigateToAsync("categories");
        await indexPage.WaitForDataGridAsync();
        await indexPage.ClickSortByNameAscendingAsync();
        await indexPage.WaitForCategoryAsync(parentName);

        // Step 5: Create child category with parent
        await indexPage.ClickCreateAsync();
        await crudPage.FillNameAndSlugAsync(childName, childSlug);
        await crudPage.SelectParentInDialogAsync(parentName);
        await crudPage.ClickAsync("#crudsave");
        await crudPage.WaitForDialogHiddenAsync();

        // Step 6: Verify both appear
        await NavigateToAsync("categories");
        await indexPage.WaitForDataGridAsync();
        await indexPage.ClickSortByNameAscendingAsync();
        await indexPage.WaitForCategoryAsync(childName);
        await indexPage.WaitForCategoryAsync(parentName);
    }

    private async Task LoginAsAdminAsync()
    {
        var loginPage = new LoginPage(Page);
        await loginPage.NavigateAsync(BaseUrl);
        await loginPage.FillLoginFormAsync(AdminUsername, AdminPassword);
        await loginPage.SubmitLoginAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitForRedirectAwayFromLoginAsync();
    }

    private async Task WaitForRedirectAwayFromLoginAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 10000)
        {
            if (!Page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase))
                return;
            await Task.Delay(200);
        }
        throw new XunitException(
            $"Should redirect from login. Current URL: {Page.Url}");
    }
}
```

### Journey Test Conventions

- **One `[Fact]` per journey** — each test is a complete user story, not a single assertion.
- **Sequential steps with comments** — label each step for readability.
- **Generate unique test data** — use `Guid.NewGuid().ToString()[..8]` to avoid collisions between parallel test runs.
- **Prefix test data with "0 "** — ensures it sorts to the top in data grids for easy verification.
- **Login helper is private per journey** — different journeys may need different users.
- **Wait helpers for redirects** — use polling loops with timeouts, not fixed `Task.Delay`.
- **Use Page Objects for ALL interactions** — never call `Page.Locator()` directly in journey tests.

---

## Playwright Configuration

```json
// appsettings.json
{
    "Playwright": {
        "Headless": true,
        "SlowMo": 0
    }
}
```

Set `Headless: false` and `SlowMo: 200` during development to watch tests execute visually. CI always runs headless.

---

## Rules & Conventions Summary

### General
1. **xUnit only** — no NUnit, no MSTest.
2. **Every test class has `[Collection(...)]`** — prevents factory conflicts.
3. **Respawn resets DB after each test** via `IAsyncLifetime.DisposeAsync()`.
4. **Testcontainers provides real SQL Server** — no in-memory database, no SQLite.

### API Integration Tests
5. **`BaseCrudControllerTests<TEntity, TDto>`** provides standard CRUD tests as virtual methods.
6. **Override with `Assert.True(true)` + doc comment** to opt out of base tests.
7. **AutoFixture for all test data** — customize with `.Without()` for nav properties, `.With()` for required fields.
8. **Generate unique field values** with embedded `Guid` fragments.
9. **Always authenticate before calling endpoints** — `SetAuthenticationForClient()` is required.

### UI E2E Tests
10. **Page Object Model is mandatory** — every page gets a class in `Pages/`.
11. **All selectors use `#id` only** — NEVER CSS classes, text, or XPath in Page Objects.
12. **Blazor components MUST have `id` attributes** on interactable elements.
13. **Always call `WaitForBlazorAsync()` after navigation** — MudBlazor needs JS interop time.
14. **Journey tests are sequential stories** — login → navigate → act → verify, in one `[Fact]`.
15. **Use `WaitForRedirectAwayFromLoginAsync()` pattern** — polling loop, not fixed delay.
16. **Viewport is always 1920x1080** — consistent across local and CI.
17. **Page Object methods wrap interactions with try/catch** — include URL in error messages.
18. **Two separate factories** — `ApiTestFactory` for API + DB, `WebTestFactory` for Blazor frontend.
