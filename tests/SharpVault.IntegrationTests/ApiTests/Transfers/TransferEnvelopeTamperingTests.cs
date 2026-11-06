// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferEnvelopeTamperingTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string NameEnvelope = "NameEnvelope";
    private const string ValueEnvelope = "ValueEnvelope";
    private const string EnvelopeSuffix = "Envelope";
    private const string PartitionKeyColumn = "PartitionKey";
    private const string RowKeyColumn = "RowKey";
    private const string TimestampColumn = "Timestamp";
    private const string OwnerBoundValue = "owner-bound-value-4410";
    private const string CopiedValue = "copied-value-5521";

    [Fact]
    public async Task SourceEnvelopeCopiedIntoTargetRow_ReadsAsDamaged()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Copied login", CopiedValue);
        var sourceName = (await ReadSecretRowAsync(TestUsers.User.ObjectId, secret.Id))[NameEnvelope];
        await TransferCompletedAsync();
        var target = await ReadSecretRowAsync(TestUsers.OtherUser.ObjectId, secret.Id);
        target[NameEnvelope] = sourceName;
        await VaultsTable.UpdateEntityAsync(target, target.ETag, TableUpdateMode.Replace, Token);

        var (response, read) = await ApiAs(TestUsers.OtherUser).GetSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(read is { IsDamaged: true });
        Assert.Null(read?.Name);
    }

    [Fact]
    public async Task SourceValueEnvelopeCopiedIntoTargetRow_RevealFailsAndTheValueIsNotReturned()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Copied value login", CopiedValue);
        var sourceValue = (await ReadSecretRowAsync(TestUsers.User.ObjectId, secret.Id))[ValueEnvelope];
        await TransferCompletedAsync();
        var target = await ReadSecretRowAsync(TestUsers.OtherUser.ObjectId, secret.Id);
        target[ValueEnvelope] = sourceValue;
        await VaultsTable.UpdateEntityAsync(target, target.ETag, TableUpdateMode.Replace, Token);

        var (reveal, body) = await ApiAs(TestUsers.OtherUser).RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.False(reveal.IsSuccessStatusCode);
        Assert.NotEqual(CopiedValue, body?.Value);
    }

    [Fact]
    public async Task TransferredEnvelope_OpenedWithSourceOwnerContext_Fails()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Owner bound login", OwnerBoundValue);
        await TransferCompletedAsync();
        var moved = await ReadSecretRowAsync(TestUsers.OtherUser.ObjectId, secret.Id);
        var reclaimed = new TableEntity(VaultKeys.PartitionKey(TestUsers.User.ObjectId), VaultKeys.SecretRowKey(secret.Id, recycled: false));
        foreach (var column in moved.Keys.Where(key => key is not (PartitionKeyColumn or RowKeyColumn or TimestampColumn)))
        {
            reclaimed[column] = moved[column];
        }

        await VaultsTable.AddEntityAsync(reclaimed, Token);

        var (read, shown) = await ApiAs(TestUsers.User).GetSecretAsync(secret.Id);
        var (reveal, body) = await ApiAs(TestUsers.User).RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.True(read.IsSuccessStatusCode);
        Assert.True(shown is { IsDamaged: true });
        Assert.False(reveal.IsSuccessStatusCode);
        Assert.NotEqual(OwnerBoundValue, body?.Value);
    }

    [Fact]
    public async Task Transfer_EveryEnvelopeHasANewNonce()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var folder = await CreateFolderAsync(TestUsers.User, "Nonce folder", parentId: null);
        var secret = await CreateSecretAsync(TestUsers.User, "Nonce login", "nonce-value-one", notes: "nonce-notes", folderId: folder.Id);
        await ReplaceValueAsync(TestUsers.User, secret, "nonce-value-two");
        var before = await ReadEnvelopesAsync(TestUsers.User.ObjectId);

        await TransferCompletedAsync();
        var after = await ReadEnvelopesAsync(TestUsers.OtherUser.ObjectId);

        Assert.True(before.Count >= 5, $"Expected at least five envelopes, found {before.Count}.");
        foreach (var (key, original) in before)
        {
            Assert.True(after.TryGetValue(key, out var moved), $"No moved envelope for {key}.");
            Assert.True(original.Length >= EnvelopeFormat.HeaderSize && moved!.Length >= EnvelopeFormat.HeaderSize, $"Envelope {key} is shorter than its header.");
            Assert.False(SameNonce(original, moved!), $"Envelope {key} kept its nonce.");
        }
    }

    private static bool SameNonce(byte[] first, byte[] second) =>
        first.AsSpan(EnvelopeFormat.VersionSize, EnvelopeFormat.NonceSize).SequenceEqual(second.AsSpan(EnvelopeFormat.VersionSize, EnvelopeFormat.NonceSize));

    private async Task TransferCompletedAsync()
    {
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);
    }

    private async Task<TableEntity> ReadSecretRowAsync(Guid owner, Guid secretId)
    {
        var response = await VaultsTable.GetEntityAsync<TableEntity>(VaultKeys.PartitionKey(owner), VaultKeys.SecretRowKey(secretId, recycled: false), cancellationToken: Token);
        return response.Value;
    }

    private async Task<Dictionary<string, byte[]>> ReadEnvelopesAsync(Guid owner)
    {
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {VaultKeys.PartitionKey(owner)}");
        var envelopes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        await foreach (var row in VaultsTable.QueryAsync<TableEntity>(filter, cancellationToken: Token))
        {
            foreach (var column in row)
            {
                if (column.Key.EndsWith(EnvelopeSuffix, StringComparison.Ordinal) && column.Value is byte[] bytes)
                {
                    envelopes[$"{row.RowKey}|{column.Key}"] = bytes;
                }
            }
        }

        return envelopes;
    }

    private async Task<SecretDto> CreateSecretAsync(TestUser owner, string name, string value, string? notes = null, Guid? folderId = null)
    {
        var body = new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = name, Value = value, Notes = notes, FolderId = folderId };
        var (response, secret) = await ApiAs(owner).CreateSecretAsync(body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return secret ?? throw new InvalidOperationException("The create response had no body.");
    }

    private async Task ReplaceValueAsync(TestUser owner, SecretDto current, string value)
    {
        var body = new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = current.Name ?? string.Empty, ReplaceValue = true, Value = value };
        var (response, _) = await ApiAs(owner).UpdateSecretAsync(current.Id, current.ConcurrencyToken, body);
        Assert.True(response.IsSuccessStatusCode, $"Update failed with {response.StatusCode}.");
    }

    private async Task<FolderDto> CreateFolderAsync(TestUser owner, string name, Guid? parentId)
    {
        var (response, folder) = await ApiAs(owner).CreateFolderAsync(new CreateFolderRequestDto { Name = name, ParentId = parentId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return folder ?? throw new InvalidOperationException("The folder response had no body.");
    }
}
