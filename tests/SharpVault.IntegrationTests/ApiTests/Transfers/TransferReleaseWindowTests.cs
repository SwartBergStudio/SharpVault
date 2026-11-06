// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Transfers;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;
using Xunit;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferReleaseWindowTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const int WindowInventoryCall = 2;

    private const int MaxRunsPerTransfer = 20;

    private const string SelectedFolderName = "Selected";

    private const string RenamedFolderName = "Renamed in the window";

    private const string CreatedFolderName = "Created in the window";

    private const string XValue = "x-original";

    private static readonly TimeSpan HourStep = TimeSpan.FromHours(1);

    [Fact]
    public async Task SecretMovedOutOfTheSelectionInTheReleaseWindow_StaysWithTheOwnerAndIsNotPublished()
    {
        var setup = await SetUpSelectionAsync();
        var owner = ApiAs(TestUsers.User);
        var moved = false;

        var (settled, fired, accepted) = await RunWindowScenarioAsync(setup.Folder.Id, async () =>
        {
            var current = Ok(await owner.GetSecretAsync(setup.X.Id));
            var (response, _) = await owner.MoveSecretAsync(setup.X.Id, current.ConcurrencyToken, new MoveSecretRequestDto { FolderId = null });
            moved = response.IsSuccessStatusCode;
            return moved;
        });

        Assert.True(fired, "The release-window hook never ran.");
        Assert.True(accepted, "The release-window action failed.");
        Assert.True(moved, "The move in the release window failed.");
        Assert.Equal(TransferStateDto.Completed, settled.State);
        var ownerIds = (await ListAllSecretsAsync(TestUsers.User)).Select(item => item.Id).ToHashSet();
        var targetIds = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id).ToHashSet();
        Assert.Contains(setup.X.Id, ownerIds);
        Assert.DoesNotContain(setup.X.Id, targetIds);
        Assert.Contains(setup.Y.Id, targetIds);
        Assert.DoesNotContain(setup.Y.Id, ownerIds);
        Assert.Equal(XValue, await RevealAsync(TestUsers.User, setup.X.Id, RevealFieldDto.Value));
    }

    [Fact]
    public async Task FolderRenamedInTheReleaseWindow_TargetGetsTheNewName()
    {
        var setup = await SetUpSelectionAsync();
        var owner = ApiAs(TestUsers.User);

        var (settled, fired, accepted) = await RunWindowScenarioAsync(setup.Folder.Id, async () =>
        {
            var (response, _) = await owner.RenameFolderAsync(setup.Folder.Id, new RenameFolderRequestDto { Name = RenamedFolderName });
            return response.IsSuccessStatusCode;
        });

        Assert.True(fired, "The release-window hook never ran.");
        Assert.True(accepted, "The release-window action failed.");
        Assert.Equal(TransferStateDto.Completed, settled.State);
        var targetFolders = Ok(await ApiAs(TestUsers.OtherUser).ListFoldersAsync()).Items;
        Assert.Contains(targetFolders, folder => folder.Name == RenamedFolderName);
        Assert.DoesNotContain(targetFolders, folder => folder.Name == SelectedFolderName);
    }

    [Fact]
    public async Task FolderCreatedInsideTheSelectionInTheReleaseWindow_IsPublishedNotLost()
    {
        var setup = await SetUpSelectionAsync();
        var owner = ApiAs(TestUsers.User);

        var (settled, fired, accepted) = await RunWindowScenarioAsync(setup.Folder.Id, async () =>
        {
            var (response, _) = await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = CreatedFolderName, ParentId = setup.Folder.Id });
            return response.IsSuccessStatusCode;
        });

        Assert.True(fired, "The release-window hook never ran.");
        Assert.True(accepted, "The release-window action failed.");
        Assert.Equal(TransferStateDto.Completed, settled.State);
        var targetFolders = Ok(await ApiAs(TestUsers.OtherUser).ListFoldersAsync()).Items;
        Assert.Contains(targetFolders, folder => folder.Name == CreatedFolderName);
    }

    private async Task<WindowRun> RunWindowScenarioAsync(Guid folderId, Func<Task<bool>> inWindow)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Guid? transferId = null;
        var windowCalls = 0;
        var windowFired = false;
        var windowAccepted = false;
        UseHost(RaceHost(clock, async (method, args) =>
        {
            if (method.Name != nameof(IVaultStore.ListInventoryAsync) || transferId is not { } watched)
            {
                return;
            }

            var stored = await Host.Services.GetRequiredService<ITransferJobStore>().GetAsync(watched, default);
            if (stored?.Value.State == TransferState.Releasing && Interlocked.Increment(ref windowCalls) == WindowInventoryCall)
            {
                windowAccepted = await inWindow();
                windowFired = true;
            }
        }, null));

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser, scope: TransferScopeKindDto.SelectedFolders, folderIds: [folderId]);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        transferId = transfer.Id;
        var settled = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        for (var run = 0; run < MaxRunsPerTransfer && settled.State != TransferStateDto.Completed; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(HourStep);
            settled = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        }

        transferId = null;
        return new WindowRun(settled, windowFired, windowAccepted);
    }

    private async Task<WindowSetup> SetUpSelectionAsync()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var owner = ApiAs(TestUsers.User);
        var folder = Ok(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = SelectedFolderName }));
        var x = Ok(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "X", Value = XValue, FolderId = folder.Id }));
        var y = Ok(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Y", Value = "y", FolderId = folder.Id }));
        return new WindowSetup(folder, x, y);
    }

    private static T Ok<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"The request failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The response had no body.");
    }

    private sealed record WindowSetup(FolderDto Folder, SecretDto X, SecretDto Y);

    private sealed record WindowRun(TransferDto Settled, bool Fired, bool Accepted);
}
