// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Storage;

public sealed record RecordedStorageRequest(string Method, string Path, string? Filter)
{
    private const string EntityQuerySuffix = "()";
    private const string PartitionKeyCondition = "PartitionKey eq";

    public bool IsEntityQuery => Method == "GET" && Path.TrimEnd('/').EndsWith(EntityQuerySuffix, StringComparison.Ordinal);

    public bool HasPartitionKeyCondition => Filter?.Contains(PartitionKeyCondition, StringComparison.Ordinal) == true;
}
