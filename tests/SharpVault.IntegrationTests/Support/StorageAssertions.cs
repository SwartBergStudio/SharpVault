// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.Support;

public static partial class StorageAssertions
{
    [GeneratedRegex(@"^/[^/]+/(Users|AuditLog)(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex UserOrAuditTablePath();

    public static void AssertOnlyUserAndAuditTablesTouched(StorageRequestRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        foreach (var request in recorder.Requests)
        {
            Assert.True(
                UserOrAuditTablePath().IsMatch(request.Path),
                $"Unexpected storage request {request.Method} {request.Path}");
        }

        Assert.Empty(recorder.QueriesWithoutPartitionKey());
    }
}
