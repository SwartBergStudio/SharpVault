// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferLeakageTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const int MaximumReasonLength = 1001;

    [Fact]
    public async Task Preview_ResponseContainsNoPlaintextMarker()
    {
        var markers = new PlaintextMarkers();
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await CreateMarkedSecretAsync(TestUsers.User, markers);

        var (response, _) = await ApiAs(TestUsers.Admin).SendAsync<TransferPreviewDto>(HttpMethod.Get, $"{ApiRoutes.AdminTransferPreview}?sourceUserId={TestUsers.User.ObjectId:D}");
        var body = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Get, $"{ApiRoutes.AdminTransferPreview}?sourceUserId={TestUsers.User.ObjectId:D}", null, null);
        var text = await body.Content.ReadAsStringAsync(Token);

        Assert.True(response.IsSuccessStatusCode, $"Preview failed with {response.StatusCode}.");
        markers.AssertAbsentFrom(text);
    }

    [Fact]
    public async Task Preview_OpensNoDataKey()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await CreateMarkedSecretAsync(TestUsers.User, new PlaintextMarkers());
        Factory.KeyWraps.Clear();

        await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Get, $"{ApiRoutes.AdminTransferPreview}?sourceUserId={TestUsers.User.ObjectId:D}", null, null);

        Assert.Equal(0, Factory.KeyWraps.Unwraps);
    }

    [Fact]
    public async Task TransferAudit_NoRowContainsAnyPlaintextMarker()
    {
        var markers = new PlaintextMarkers();
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await CreateMarkedSecretAsync(TestUsers.User, markers);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser, reason: ValidReason);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var rows = new List<TableEntity>();
        rows.AddRange(await ListAuditRowsAsync());
        rows.AddRange(await TableRowsAsync(TableNames.SecretAuditLog));
        rows.AddRange(await TableRowsAsync(TableNames.TransferJobs));
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            markers.AssertAbsentFrom(RowText(row));
        }
    }

    [Fact]
    public async Task Job_LogsContainNoPlaintextMarkerOrReason()
    {
        var markers = new PlaintextMarkers();
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await CreateMarkedSecretAsync(TestUsers.User, markers);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser, reason: ValidReason);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        Factory.Logs.Clear();

        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        foreach (var marker in markers.All)
        {
            Assert.False(Factory.Logs.ContainsText(marker), "A plaintext marker reached the logs.");
        }

        Assert.False(Factory.Logs.ContainsText(ValidReason), "The transfer reason reached the logs.");
    }

    [Fact]
    public async Task Create_ValidationError_DoesNotEchoTheReason()
    {
        var markers = new PlaintextMarkers();
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser, reason: markers.Name + "\u0007"));
        var text = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(markers.Name, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ReasonWithRawLoneSurrogateEscape_Returns400AndStoresNothing()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        const string loneSurrogateEscape = "Role change with a lone \\uD800 surrogate";
        var json = CreateJsonBody(TestUsers.User, TestUsers.OtherUser).Replace(ValidReason, loneSurrogateEscape, StringComparison.Ordinal);

        var response = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Post, ApiRoutes.AdminTransfers, json, null);
        var text = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("lone", text, StringComparison.Ordinal);
        var (_, page) = await ApiAs(TestUsers.Admin).SendAsync<TransferPageDto>(HttpMethod.Get, ApiRoutes.AdminTransfers);
        Assert.NotNull(page);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Create_ReasonWithODataQuote_IsStoredVerbatim()
    {
        const string reason = "Owner's request, filter ' or PartitionKey ne 'x' for the record";
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);

        var (response, body) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser, reason: reason));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(reason, body?.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   short   ")]
    [InlineData("Role change\u0007 with a control character")]
    public async Task Create_InvalidReason_Returns400(string reason)
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser, reason: reason));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReasonTooLong_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser, reason: new string('r', MaximumReasonLength)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateFolderIds_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var folder = Guid.NewGuid();

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser, scope: TransferScopeKindDto.SelectedFolders, folderIds: [folder, folder]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_FolderIdsWithAllItems_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser, scope: TransferScopeKindDto.AllItems, folderIds: [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_TooManyFolderIds_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var folders = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser, scope: TransferScopeKindDto.SelectedFolders, folderIds: folders));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyGuidSource_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.OtherUser);
        var body = CreateBody(TestUsers.User, TestUsers.OtherUser);
        body.SourceUserId = Guid.Empty;

        var (response, _) = await CreateRawAsync(TestUsers.Admin, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NumericEnum_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var numericJson = $"{{\"sourceUserId\":\"{TestUsers.User.ObjectId:D}\",\"targetUserId\":\"{TestUsers.OtherUser.ObjectId:D}\",\"scope\":{(int)TransferScopeKindDto.AllItems},\"category\":{(int)ReasonCategoryDto.RoleChange},\"reason\":\"{ValidReason}\"}}";

        var numeric = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Post, ApiRoutes.AdminTransfers, numericJson, null);
        var named = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Post, ApiRoutes.AdminTransfers, CreateJsonBody(TestUsers.User, TestUsers.OtherUser), null);

        Assert.Equal(HttpStatusCode.BadRequest, numeric.StatusCode);
        Assert.Equal(HttpStatusCode.Created, named.StatusCode);
    }

    [Fact]
    public async Task Get_MalformedId_Returns404()
    {
        await WarmUpAllAsync(TestUsers.Admin);

        var response = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Get, "/api/admin/transfers/not-a-guid", null, null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_SourceUserIdNotAGuid_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin);

        var response = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Get, $"{ApiRoutes.AdminTransferPreview}?sourceUserId=abc", null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListTransfers_MalformedToken_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin);

        var response = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Get, $"{ApiRoutes.AdminTransfers}?continuationToken=not-a-real-token", null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_OnlyOneAdmin_Returns400WithTheSecondAdminRule()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var response = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Post, ApiRoutes.AdminTransfers, CreateJsonBody(TestUsers.User, TestUsers.OtherUser), null);
        var text = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(TransferMessages.SecondAdminRequired, text, StringComparison.Ordinal);
    }

    private async Task CreateMarkedSecretAsync(TestUser owner, PlaintextMarkers markers)
    {
        var body = new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            Username = markers.Username,
            Url = "https://" + markers.Url + ".example.test",
            Value = markers.Value,
            Notes = markers.Notes,
            Tags = [markers.Tag],
        };
        var (response, _) = await ApiAs(owner).CreateSecretAsync(body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<IReadOnlyList<TableEntity>> TableRowsAsync(string tableName)
    {
        var rows = new List<TableEntity>();
        await foreach (var row in GetTableClient(tableName).QueryAsync<TableEntity>(cancellationToken: Token))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static string RowText(TableEntity row) =>
        string.Join('\n', row.Select(pair => pair.Value switch
        {
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        }));
}
