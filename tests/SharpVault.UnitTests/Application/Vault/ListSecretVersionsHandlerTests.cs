// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.History.Queries;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class ListSecretVersionsHandlerTests : VaultHandlerTestBase
{
    private ListSecretVersionsQueryHandler Handler() => new(Store, Policy);

    [Fact]
    public async Task Handle_SecretMissing_ReturnsNotFound()
    {
        var result = await Handler().Handle(new ListSecretVersionsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await Store.DidNotReceive().ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortsNewestFirst()
    {
        var secret = GivenSecret();
        var oldest = NewVersion(secret.Value, Now.AddDays(-9));
        var newest = NewVersion(secret.Value, Now.AddDays(-1));
        var middle = NewVersion(secret.Value, Now.AddDays(-5));
        GivenVersions(secret.Value.Id, middle, oldest, newest);

        var result = await Handler().Handle(new ListSecretVersionsQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], result.Value!.Items.Select(item => item.VersionId));
    }

    [Fact]
    public async Task Handle_SameTimestamp_OrdersByIdDescending()
    {
        var secret = GivenSecret();
        var at = Now.AddDays(-2);
        var versions = Enumerable.Range(0, 4).Select(_ => NewVersion(secret.Value, at)).ToArray();
        GivenVersions(secret.Value.Id, versions);

        var result = await Handler().Handle(new ListSecretVersionsQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(versions.Select(version => version.Id).OrderDescending(), result.Value!.Items.Select(item => item.VersionId));
    }

    [Fact]
    public async Task Handle_NoVersions_ReturnsEmptyList()
    {
        var secret = GivenSecret();
        GivenVersions(secret.Value.Id);

        var result = await Handler().Handle(new ListSecretVersionsQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task Handle_MapsVersionIdRevisionAndReplacedAt()
    {
        var secret = GivenSecret();
        var version = NewVersion(secret.Value, Now.AddHours(-3));
        version.Revision = 11;
        GivenVersions(secret.Value.Id, version);

        var result = await Handler().Handle(new ListSecretVersionsQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(version.Id, item.VersionId);
        Assert.Equal(11, item.Revision);
        Assert.Equal(Now.AddHours(-3), item.ReplacedAt);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Items_NeverCarryValues()
    {
        var secret = GivenSecret();
        var marker = "SV-VALUE-" + Guid.NewGuid().ToString("N");
        GivenVersions(secret.Value.Id, NewVersion(secret.Value, plaintext: marker));

        var result = await Handler().Handle(new ListSecretVersionsQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(marker, JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NullGrant_ReturnsNotFoundWithoutStoreCall()
    {
        GivenNoGrant();
        var secret = GivenSecret();

        var result = await Handler().Handle(new ListSecretVersionsQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_AsksThePolicyForView()
    {
        var secret = GivenSecret();
        GivenVersions(secret.Value.Id);

        await Handler().Handle(new ListSecretVersionsQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal([(secret.Value.Id, SecretPermission.View)], Policy.Requests);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ReadsFromTheGrantedOwnersPartition()
    {
        var other = Guid.NewGuid();
        GivenGrant(SecretPermission.View, other);
        var secret = GivenSecret(partition: other);

        await Handler().Handle(new ListSecretVersionsQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        await Store.Received().ListVersionsAsync(other, secret.Value.Id, Arg.Any<CancellationToken>());
        await Store.DidNotReceive().ListVersionsAsync(Owner, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
