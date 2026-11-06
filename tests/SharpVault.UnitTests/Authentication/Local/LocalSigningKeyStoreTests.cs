// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Authentication.Local;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Authentication.Local;

[Trait("Category", "Security")]
[Collection(ProcessSpawningTests.Name)]
public sealed class LocalSigningKeyStoreTests : IDisposable
{
    private const string KeyFileName = "dev-auth-key";
    private const string WorkerPathVariable = "SHARPVAULT_KEY_WORKER_PATH";
    private const string WorkerStartVariable = "SHARPVAULT_KEY_WORKER_START";
    private const string WorkerResultVariable = "SHARPVAULT_KEY_WORKER_RESULT";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sharpvault-key-" + Guid.NewGuid().ToString("N"));

    private string KeyPath => Path.Combine(_directory, "nested", KeyFileName);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static byte[] Material(LocalSigningKey key) => key.Key.Key;

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        File.WriteAllText(KeyPath, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void LoadOrCreate_MissingFile_CreatesSixtyFourByteKey()
    {
        var key = LocalSigningKeyStore.LoadOrCreate(KeyPath);

        Assert.True(File.Exists(KeyPath));
        Assert.Equal(64, Material(key).Length);
        Assert.Equal(64, Convert.FromBase64String(File.ReadAllText(KeyPath).Trim()).Length);
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_ReturnsSameKeyAndDoesNotRewrite()
    {
        var first = LocalSigningKeyStore.LoadOrCreate(KeyPath);
        var writtenAt = File.GetLastWriteTimeUtc(KeyPath);
        var content = File.ReadAllText(KeyPath);

        var second = LocalSigningKeyStore.LoadOrCreate(KeyPath);

        Assert.Equal(Material(first), Material(second));
        Assert.Equal(content, File.ReadAllText(KeyPath));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(KeyPath));
    }

    [Fact]
    public async Task LoadOrCreate_ParallelCallers_AllGetTheSameKey()
    {
        var keys = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => LocalSigningKeyStore.LoadOrCreate(KeyPath))));

        var distinct = keys.Select(key => Convert.ToBase64String(Material(key))).Distinct().ToList();
        Assert.Single(distinct);
        Assert.Equal(distinct[0], File.ReadAllText(KeyPath).Trim());
    }

    [Fact]
    public async Task LoadOrCreate_ParallelCallers_LeaveNoTempFiles()
    {
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => LocalSigningKeyStore.LoadOrCreate(KeyPath))));

        var files = Directory.GetFiles(Path.GetDirectoryName(KeyPath)!);
        Assert.Equal([KeyPath], files);
    }

    [Fact]
    public async Task LoadOrCreate_ManyRoundsOfParallelFirstCreation_NeverThrowsAndEveryCallerGetsTheSameKey()
    {
        const int rounds = 100;
        const int callersPerRound = 32;
        var failures = new List<string>();

        for (var round = 0; round < rounds; round++)
        {
            var path = Path.Combine(_directory, $"round-{round}", KeyFileName);
            var outcomes = await Task.WhenAll(Enumerable.Range(0, callersPerRound).Select(_ => Task.Run(() => Attempt(path))));
            failures.AddRange(outcomes.Where(outcome => outcome.Error is not null).Select(outcome => outcome.Error!));
            var distinct = outcomes.Where(outcome => outcome.Key is not null).Select(outcome => outcome.Key).Distinct().ToList();
            Assert.Single(distinct);
            Assert.Equal(distinct[0], File.ReadAllText(path).Trim());
        }

        Assert.Empty(failures.Distinct());
    }

    [Fact]
    public async Task LoadOrCreate_ParallelFirstCreationAcrossProcesses_NeverThrowsAndEveryProcessGetsTheSameKey()
    {
        const int rounds = 4;
        const int processesPerRound = 6;
        var executable = Path.Combine(AppContext.BaseDirectory, "SharpVault.UnitTests.exe");
        if (!File.Exists(executable))
        {
            Assert.Skip("The test host executable is not available.");
            return;
        }

        for (var round = 0; round < rounds; round++)
        {
            var path = Path.Combine(_directory, $"process-round-{round}", KeyFileName);
            var startAt = DateTime.UtcNow.AddSeconds(4).Ticks;
            var workers = Enumerable.Range(0, processesPerRound)
                .Select(index => RunWorkerAsync(executable, path, startAt, Path.Combine(_directory, $"result-{round}-{index}")))
                .ToList();
            var exitCodes = await Task.WhenAll(workers);

            Assert.All(exitCodes, code => Assert.Equal(0, code));
            var keys = Enumerable.Range(0, processesPerRound)
                .Select(index => File.ReadAllText(Path.Combine(_directory, $"result-{round}-{index}")))
                .Distinct()
                .ToList();
            Assert.Single(keys);
            Assert.Equal(keys[0], File.ReadAllText(path).Trim());
        }
    }

    [Fact]
    public void CrossProcessWorker_WhenConfigured_LoadsTheKeyAndRecordsIt()
    {
        var path = Environment.GetEnvironmentVariable(WorkerPathVariable);
        if (path is null)
        {
            return;
        }

        var startAt = long.Parse(Environment.GetEnvironmentVariable(WorkerStartVariable)!, System.Globalization.CultureInfo.InvariantCulture);
        while (DateTime.UtcNow.Ticks < startAt)
        {
            Thread.SpinWait(100);
        }

        var key = LocalSigningKeyStore.LoadOrCreate(path);

        File.WriteAllText(Environment.GetEnvironmentVariable(WorkerResultVariable)!, Convert.ToBase64String(Material(key)));
    }

    private static async Task<int> RunWorkerAsync(string executable, string keyPath, long startAt, string resultPath)
    {
        var info = new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-method");
        info.ArgumentList.Add(typeof(LocalSigningKeyStoreTests).FullName + "." + nameof(CrossProcessWorker_WhenConfigured_LoadsTheKeyAndRecordsIt));
        info.Environment[WorkerPathVariable] = keyPath;
        info.Environment[WorkerStartVariable] = startAt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        info.Environment[WorkerResultVariable] = resultPath;

        using var process = System.Diagnostics.Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        return process.ExitCode;
    }

    private static (string? Key, string? Error) Attempt(string path)
    {
        try
        {
            var key = LocalSigningKeyStore.LoadOrCreate(path);
            return (Convert.ToBase64String(Material(key)), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, exception.GetType().Name + ": " + exception.Message.Replace(path, "<path>", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void LoadOrCreate_EmptyFile_ThrowsAndLeavesFileUnchanged()
    {
        WriteFile(string.Empty);

        Assert.Throws<InvalidOperationException>(() => LocalSigningKeyStore.LoadOrCreate(KeyPath));

        Assert.Equal(string.Empty, File.ReadAllText(KeyPath));
    }

    [Fact]
    public void LoadOrCreate_NotBase64_Throws()
    {
        WriteFile("this is not base64 !!!");

        Assert.Throws<InvalidOperationException>(() => LocalSigningKeyStore.LoadOrCreate(KeyPath));
    }

    [Fact]
    public void LoadOrCreate_WrongLength_Throws()
    {
        WriteFile(Convert.ToBase64String(new byte[32]));

        Assert.Throws<InvalidOperationException>(() => LocalSigningKeyStore.LoadOrCreate(KeyPath));
    }

    [Fact]
    public void LoadOrCreate_WhitespaceAroundKey_IsAccepted()
    {
        var bytes = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();
        WriteFile("  " + Convert.ToBase64String(bytes) + "\r\n");

        var key = LocalSigningKeyStore.LoadOrCreate(KeyPath);

        Assert.Equal(bytes, Material(key));
    }

    [Fact]
    public void LoadOrCreate_UnixNewFile_HasOwnerOnlyMode()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only.");
            return;
        }


        LocalSigningKeyStore.LoadOrCreate(KeyPath);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
    }

    [Fact]
    public void LoadOrCreate_UnixGroupReadableFile_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only.");
            return;
        }

        LocalSigningKeyStore.LoadOrCreate(KeyPath);
        File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        var error = Assert.Throws<InvalidOperationException>(() => LocalSigningKeyStore.LoadOrCreate(KeyPath));

        Assert.Contains("chmod 600", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadOrCreate_Created_ExceptionsContainNoKeyMaterial()
    {
        var key = LocalSigningKeyStore.LoadOrCreate(KeyPath);
        var encoded = Convert.ToBase64String(Material(key));
        File.WriteAllText(KeyPath, encoded[..^8]);

        var error = Assert.Throws<InvalidOperationException>(() => LocalSigningKeyStore.LoadOrCreate(KeyPath));

        Assert.DoesNotContain(encoded[..16], error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPath_Always_IsUnderLocalApplicationDataAndOutsideTheRepository()
    {
        var path = LocalSigningKeyStore.DefaultPath();

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(localData, path, StringComparison.Ordinal);
        Assert.EndsWith(KeyFileName, path, StringComparison.Ordinal);
        Assert.False(path.StartsWith(SharpVault.UnitTests.Support.RepositoryRoot.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Key_ToString_RevealsNothing()
    {
        var key = LocalSigningKeyStore.LoadOrCreate(KeyPath);

        var text = key.ToString();

        Assert.DoesNotContain(Convert.ToBase64String(Material(key)), text, StringComparison.Ordinal);
        Assert.Equal("LocalSigningKey", text);
    }

    [Fact]
    public void LoadOrCreate_ReturnedKey_EqualsFileBytesAndIsNotAllZero()
    {
        var created = LocalSigningKeyStore.LoadOrCreate(KeyPath);
        var fileBytes = Convert.FromBase64String(File.ReadAllText(KeyPath).Trim());

        var loaded = LocalSigningKeyStore.LoadOrCreate(KeyPath);

        Assert.Equal(fileBytes, Material(created));
        Assert.Equal(fileBytes, Material(loaded));
        Assert.Contains(Material(created), value => value != 0);
        Assert.Contains(Material(loaded), value => value != 0);
    }
}
