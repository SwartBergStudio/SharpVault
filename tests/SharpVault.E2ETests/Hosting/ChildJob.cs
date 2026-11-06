// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVault.E2ETests.Hosting;

[SupportedOSPlatform("windows")]
internal static partial class ChildJob
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;
    private const string Kernel32 = "kernel32.dll";

    private static readonly nint Handle = Create();

    public static void Assign(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!AssignProcessToJobObject(Handle, process.Handle))
        {
            throw new InvalidOperationException($"The E2E child process could not join the kill-on-close job (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
    }

    private static nint Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            return nint.Zero;
        }

        var job = CreateJobObject(nint.Zero, null);
        if (job == nint.Zero)
        {
            throw new InvalidOperationException($"The E2E kill-on-close job could not be created (Win32 error {Marshal.GetLastPInvokeError()}).");
        }

        var limits = new ExtendedLimit();
        limits.Basic.LimitFlags = KillOnJobClose;
        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimit>()))
        {
            throw new InvalidOperationException($"The E2E kill-on-close job could not be configured (Win32 error {Marshal.GetLastPInvokeError()}).");
        }

        return job;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(Kernel32, EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObject(nint attributes, string? name);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimit info, uint length);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations;
        public ulong WriteOperations;
        public ulong OtherOperations;
        public ulong ReadBytes;
        public ulong WriteBytes;
        public ulong OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
