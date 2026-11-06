// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;

namespace SharpVault.E2ETests.Hosting;

public sealed class E2EProcess(Process process, ProcessLog log) : IAsyncDisposable
{
    private const int ExitWaitMilliseconds = 15000;

    public ProcessLog Log { get; } = log;

    public static E2EProcess Start(string fileName, IEnumerable<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var log = new ProcessLog();
        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) => log.Append(args.Data);
        process.ErrorDataReceived += (_, args) => log.Append(args.Data);
        process.Start();
        if (OperatingSystem.IsWindows())
        {
            ChildJob.Assign(process);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new E2EProcess(process, log);
    }

    public bool HasExited => process.HasExited;

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        using var timeout = new CancellationTokenSource(ExitWaitMilliseconds);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
