// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Secrets.Queries;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class GetSecretHandlerTests : VaultHandlerTestBase
{
    private GetSecretQueryHandler Handler() => new(Store, KeyRings, Policy);

    [Fact]
    public async Task Handle_Existing_ReturnsDecryptedMetadataAndTheStoredETag()
    {
        var secret = GivenSecret(SecretWith("Prod DB", "admin", "https://db.test/", "prod", "db"), ETagOf(7));

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal("Prod DB", dto.Name);
        Assert.Equal("admin", dto.Username);
        Assert.Equal("https://db.test/", dto.Url);
        Assert.Equal(["prod", "db"], dto.Tags);
        Assert.Equal(ETagOf(7), dto.ConcurrencyToken);
        Assert.False(dto.IsDamaged);
        Assert.True(dto.HasValue);
        Assert.True(dto.HasNotes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Existing_NeverCarriesValueOrNotesPlaintext()
    {
        var secret = GivenSecret();
        var value = DecryptCurrentValue(secret.Value);

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(value, JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_Missing_ReturnsNotFound()
    {
        var result = await Handler().Handle(new GetSecretQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsDamaged()
    {
        GivenNoKeyRing();
        var secret = GivenSecret();

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDamaged);
        Assert.Null(result.Value.Name);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DecryptionFails_ReturnsDamagedWithNullFields()
    {
        var secret = GivenSecret(DamagedSecret());

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.True(dto.IsDamaged);
        Assert.Null(dto.Name);
        Assert.Null(dto.Username);
        Assert.Null(dto.Url);
        Assert.Empty(dto.Tags);
        Assert.Equal(secret.ETag, dto.ConcurrencyToken);
    }

    [Fact]
    public async Task Handle_MetadataKeyVersionMismatch_ReturnsDamaged()
    {
        var plain = SecretWith("Name");
        plain.MetadataKeyVersion = Ring.MetadataKeyVersion + 1;
        var secret = GivenSecret(plain);

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.Value!.IsDamaged);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_NullGrant_ReturnsNotFoundWithoutStoreCall()
    {
        GivenNoGrant();
        var secret = GivenSecret();

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GrantBelowView_ReturnsNotFoundWithoutStoreCall()
    {
        GivenGrant(SecretPermission.None, Owner);
        var secret = GivenSecret();

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_AsksThePolicyForViewOnTheRequestedSecret()
    {
        var secret = GivenSecret();

        await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal([(secret.Value.Id, SecretPermission.View)], Policy.Requests);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ReadsFromTheGrantedOwnersPartitionNotTheCallers()
    {
        var other = Guid.NewGuid();
        GivenGrant(SecretPermission.View, other);
        var secret = GivenSecret(partition: other);

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await Store.Received().GetSecretAsync(other, secret.Value.Id, Arg.Any<CancellationToken>());
        await Store.DidNotReceive().GetSecretAsync(Owner, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await Store.DidNotReceive().GetKeyRingAsync(Owner, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SecretOfAnotherOwnerWithOwnerGrantOnly_ReturnsNotFound()
    {
        var secret = GivenSecret(partition: Guid.NewGuid());

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_UnreadableKeyRing_ReturnsTheRowAsDamaged()
    {
        var secret = GivenSecret(SecretWith("Prod DB"));
        GivenUnreadableKeyRing();

        var result = await Handler().Handle(new GetSecretQuery(secret.Value.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDamaged);
        Assert.Null(result.Value.Name);
    }
}
