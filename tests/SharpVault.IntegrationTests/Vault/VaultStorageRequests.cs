// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.Vault;

public static partial class VaultStorageRequests
{
    private const string Post = "POST";
    private const string Get = "GET";

    public static IReadOnlyList<RecordedStorageRequest> VaultRequests(this StorageRequestRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        return [.. recorder.Requests.Where(IsVaultRequest)];
    }

    public static IReadOnlyList<RecordedStorageRequest> AuditWrites(this StorageRequestRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        return [.. recorder.Requests.Where(request => request.Method == Post && AuditPath().IsMatch(request.Path))];
    }

    public static IReadOnlyList<RecordedStorageRequest> VaultReads(this StorageRequestRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        return [.. recorder.VaultRequests().Where(request => request.Method == Get)];
    }

    public static IReadOnlyList<RecordedStorageRequest> VaultWrites(this StorageRequestRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        return [.. recorder.VaultRequests().Where(request => request.Method != Get)];
    }

    public static IReadOnlyList<RecordedStorageRequest> ShareInboxRequests(this StorageRequestRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        return [.. recorder.Requests.Where(request => ShareInboxPath().IsMatch(request.Path))];
    }

    public static bool IsVaultRequest(RecordedStorageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return VaultPath().IsMatch(request.Path) || BatchPath().IsMatch(request.Path);
    }

    [GeneratedRegex(@"^/[^/]+/(Vaults|SecretAuditLog)(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex VaultPath();

    [GeneratedRegex(@"^/[^/]+/ShareInbox(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ShareInboxPath();

    [GeneratedRegex(@"^/[^/]+/\$batch$", RegexOptions.CultureInvariant)]
    private static partial Regex BatchPath();

    [GeneratedRegex(@"^/[^/]+/(AuditLog|SecretAuditLog)(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex AuditPath();
}
