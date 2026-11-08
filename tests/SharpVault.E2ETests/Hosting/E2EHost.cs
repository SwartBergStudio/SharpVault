// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Microsoft.Playwright;
using SharpVault.E2ETests.Support;
using Testcontainers.Azurite;

namespace SharpVault.E2ETests.Hosting;

public class E2EHost : IAsyncLifetime
{
    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";
    public const int ViewportWidth = 1920;
    public const int ViewportHeight = 1080;
    public const string ChromeChannel = "chrome";
    public const string HealthPath = "/health";
    public const string EnvironmentName = "Development";
    public const string RateLimitHighValue = "100000";

    private const string ApiProjectPath = "src/SharpVault.Api";
    private const string WebProjectPath = "src/SharpVault.Web";
    private const string ApiAssembly = "SharpVault.Api.dll";
    private const string WebAssembly = "SharpVault.Web.dll";
    private const string TargetFramework = "net10.0";
    private const string DotnetCommand = "dotnet";
    private const string KeyFileName = "dev-auth-key";
    private const string KeyAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int KeyMaterialLength = 64;
    private const int HealthPollMilliseconds = 500;
    private readonly string _webGatewayKey = NewKeyMaterial();
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] RateLimitSettings =
    [
        "RequestsPerMinute",
        "RevealsPerMinute",
        "ShareWritesPerMinute",
        "DirectorySearchesPerMinute",
        "AdminActionsPerMinute",
        "OneTimeLinkOpensPerMinute",
    ];

    private readonly AzuriteContainer _azurite = new AzuriteBuilder(AzuriteImage).WithInMemoryPersistence().Build();
    private readonly string _workDirectory = Path.Combine(Path.GetTempPath(), "sharpvault-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly List<E2EProcess> _processes = [];
    private readonly List<E2ESession> _sessions = [];
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string ApiUrl { get; private set; } = string.Empty;

    public string WebUrl { get; private set; } = string.Empty;

    public string StorageConnectionString => _azurite.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_workDirectory);
        await _azurite.StartAsync(cancellationToken);

        var apiPort = E2EPorts.FreeLoopbackPort();
        var webPort = E2EPorts.FreeLoopbackPort();
        ApiUrl = $"http://localhost:{apiPort}";
        WebUrl = $"https://localhost:{webPort}";
        var keyFile = Path.Combine(_workDirectory, KeyFileName);
        var password = E2ECertificate.NewPassword();
        var certificate = E2ECertificate.Create(_workDirectory, password);

        var root = RepositoryLocator.FindRoot();
        var configuration = RepositoryLocator.BuildConfiguration();
        var apiDirectory = Path.Combine(root, ApiProjectPath, "bin", configuration, TargetFramework);
        var webDirectory = Path.Combine(root, WebProjectPath, "bin", configuration, TargetFramework);

        var api = StartProcess(apiDirectory, ApiAssembly, ApiEnvironment(keyFile));
        await WaitForHealthAsync(api, ApiUrl + HealthPath, cancellationToken);

        var web = StartProcess(webDirectory, WebAssembly, WebEnvironment(keyFile, certificate, password, webPort));
        await WaitForHealthAsync(web, WebUrl + HealthPath, cancellationToken);

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = ChromeChannel,
            Headless = true,
        });
    }

    public async Task<E2ESession> OpenSessionAsync()
    {
        ArgumentNullException.ThrowIfNull(_browser);

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = ViewportWidth, Height = ViewportHeight },
        });
        var session = new E2ESession(context);
        await session.InitializeAsync();
        _sessions.Add(session);
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();

        foreach (var process in _processes)
        {
            await process.DisposeAsync();
        }

        await _azurite.DisposeAsync();
        DeleteWorkDirectory();
        GC.SuppressFinalize(this);
    }

    private Dictionary<string, string> ApiEnvironment(string keyFile)
    {
        var environment = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = EnvironmentName,
            ["ASPNETCORE_URLS"] = ApiUrl,
            ["LocalAuth__KeyFilePath"] = keyFile,
            ["ConnectionStrings__tables"] = _azurite.GetConnectionString(),
            ["AzureAd__TenantId"] = string.Empty,
            ["AzureAd__ClientId"] = string.Empty,
            ["UserAccess__CacheTtl"] = "00:00:00",
            ["Encryption__DevelopmentKeyMaterial"] = NewKeyMaterial(),
            ["WebGateway__Key"] = _webGatewayKey,
            ["OneTimeLinks__Enabled"] = "true",
        };
        foreach (var setting in RateLimitSettings)
        {
            environment[$"VaultRateLimits__{setting}"] = RateLimitHighValue;
        }

        foreach (var (key, value) in ExtraApiSettings)
        {
            environment[key] = value;
        }

        return environment;
    }

    protected virtual IReadOnlyDictionary<string, string> ExtraApiSettings { get; } = new Dictionary<string, string>();

    protected virtual IReadOnlyDictionary<string, string> ExtraWebSettings { get; } = new Dictionary<string, string>();

    private Dictionary<string, string> WebEnvironment(string keyFile, string certificate, string password, int webPort)
    {
        var environment = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = EnvironmentName,
            ["ASPNETCORE_URLS"] = $"https://localhost:{webPort}",
            ["ASPNETCORE_Kestrel__Certificates__Default__Path"] = certificate,
            ["ASPNETCORE_Kestrel__Certificates__Default__Password"] = password,
            ["LocalAuth__KeyFilePath"] = keyFile,
            ["SharpVaultApi__BaseAddress"] = ApiUrl + "/",
            ["AzureAd__TenantId"] = string.Empty,
            ["AzureAd__ClientId"] = string.Empty,
            ["WebGateway__Key"] = _webGatewayKey,
            ["OneTimeLinks__Enabled"] = "true",
        };
        foreach (var (key, value) in ExtraWebSettings)
        {
            environment[key] = value;
        }

        return environment;
    }

    private E2EProcess StartProcess(string directory, string assembly, Dictionary<string, string> environment)
    {
        var process = E2EProcess.Start(DotnetCommand, [Path.Combine(directory, assembly)], directory, environment);
        _processes.Add(process);
        return process;
    }

    private static async Task WaitForHealthAsync(E2EProcess process, string url, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupTimeout);
        try
        {
            while (true)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"The process for {url} exited during start-up.{Environment.NewLine}{process.Log}");
                }

                if (await IsHealthyAsync(client, url, timeout.Token))
                {
                    return;
                }

                await Task.Delay(HealthPollMilliseconds, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{url} did not become healthy within {StartupTimeout.TotalSeconds} seconds.{Environment.NewLine}{process.Log}");
        }
    }

    private static async Task<bool> IsHealthyAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void DeleteWorkDirectory()
    {
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string NewKeyMaterial() =>
        string.Create(KeyMaterialLength, KeyAlphabet, (span, alphabet) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
}
