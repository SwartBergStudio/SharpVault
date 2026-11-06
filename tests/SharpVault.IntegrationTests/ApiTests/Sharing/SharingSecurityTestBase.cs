// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.ApiTests.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

public abstract partial class SharingSecurityTestBase(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    protected static readonly TestUser Owner = TestUsers.User;

    protected SharingTestApi SharingFor(TestUser user) => new(ClientFor(user));

    protected static CreateShareGrantRequestDto GrantBody(TestUser recipient, SharePermissionDto permission) =>
        new() { RecipientId = recipient.ObjectId, Permission = permission };

    protected async Task<SecretDto> NewSecretAsync(PlaintextMarkers markers, Guid? folderId = null, TestUser? owner = null) =>
        await CreateSecretAsync(ApiFor(owner ?? Owner), markers, folderId);

    protected async Task<FolderDto> NewFolderAsync(string name, Guid? parentId = null, TestUser? owner = null) =>
        Require(await ApiFor(owner ?? Owner).CreateFolderAsync(new CreateFolderRequestDto { Name = name, ParentId = parentId }));

    protected async Task<ShareGrantDto> ShareSecretAsync(Guid secretId, TestUser recipient, SharePermissionDto permission, TestUser? by = null, Guid? ownerId = null) =>
        Require(await SharingFor(by ?? Owner).CreateSecretGrantAsync(ownerId ?? Owner.ObjectId, secretId, GrantBody(recipient, permission)));

    protected async Task<ShareGrantDto> ShareFolderAsync(Guid folderId, TestUser recipient, SharePermissionDto permission, TestUser? by = null, Guid? ownerId = null) =>
        Require(await SharingFor(by ?? Owner).CreateFolderGrantAsync(ownerId ?? Owner.ObjectId, folderId, GrantBody(recipient, permission)));

    protected async Task<HttpStatusCode> SharedGetStatusAsync(TestUser caller, Guid secretId, Guid? ownerId = null) =>
        (await SharingFor(caller).GetSharedSecretAsync(ownerId ?? Owner.ObjectId, secretId)).Response.StatusCode;

    protected async Task<HttpStatusCode> SharedFolderStatusAsync(TestUser caller, Guid folderId, Guid? ownerId = null) =>
        (await SharingFor(caller).GetSharedFolderAsync(ownerId ?? Owner.ObjectId, folderId)).Response.StatusCode;

    protected async Task<HttpStatusCode> SharedRevealStatusAsync(TestUser caller, Guid secretId, Guid? ownerId = null) =>
        (await SharingFor(caller).RevealSharedSecretAsync(ownerId ?? Owner.ObjectId, secretId, ViewValue)).Response.StatusCode;

    protected static UpdateSecretRequestDto RenameBody(string name) => new() { Type = SecretTypeDto.Login, Name = name };

    protected async Task<StateSnapshot> SnapshotAsync()
    {
        var rows = new List<string>();
        foreach (var table in new[] { TableNames.Vaults, TableNames.ShareInbox, TableNames.SecretAuditLog })
        {
            await foreach (var row in GetTableClient(table).QueryAsync<TableEntity>(cancellationToken: Token))
            {
                rows.Add($"{table}|{row.PartitionKey}|{row.RowKey}|{row.ETag}");
            }
        }

        var audit = (await ListAuditRowsAsync())
            .Count(row => row.GetString("EventType") is { } name && name != nameof(AuditEventType.SignIn));
        return new StateSnapshot([.. rows.Order(StringComparer.Ordinal)], audit);
    }

    protected static string NormaliseBody(string body) => GuidAndTrace().Replace(body, "X");

    protected static async Task<string> NormalisedAsync(HttpResponseMessage response) =>
        NormaliseBody(await response.Content.ReadAsStringAsync(Token));

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\"traceId\":\"[^\"]*\"", RegexOptions.CultureInvariant)]
    private static partial Regex GuidAndTrace();

    protected sealed record StateSnapshot(IReadOnlyList<string> Rows, int AuditRows);
}
