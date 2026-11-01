// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Audit;

[Trait("Category", "Security")]
public sealed class AuditLogContractTests
{
    private static readonly string[] ForbiddenFragments =
    [
        "UpdateEntity",
        "UpsertEntity",
        "DeleteEntity",
        "SubmitTransaction",
        "ETag.All",
    ];

    [Fact]
    public void AuditLog_Interface_ExposesOnlyAppend()
    {
        var methods = typeof(IAuditLog).GetMethods();

        var method = Assert.Single(methods);
        Assert.Equal("AppendAsync", method.Name);
        Assert.Empty(typeof(IAuditLog).GetProperties());
        Assert.Empty(typeof(IAuditLog).GetEvents());
    }

    [Fact]
    public void TableAuditLog_Source_UsesNoUpdateUpsertOrDelete()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Infrastructure", "Audit", "TableAuditLog.cs"));

        Assert.All(ForbiddenFragments, fragment => Assert.DoesNotContain(fragment, source, StringComparison.Ordinal));
        Assert.Contains("AddEntityAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditFolder_Sources_UseNoUpdateUpsertOrDelete()
    {
        var folder = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Infrastructure", "Audit");

        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            Assert.All(ForbiddenFragments, fragment => Assert.DoesNotContain(fragment, source, StringComparison.Ordinal));
        }
    }
}
