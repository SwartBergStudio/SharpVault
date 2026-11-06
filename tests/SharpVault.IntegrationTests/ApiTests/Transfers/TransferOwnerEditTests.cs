// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferOwnerEditTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string StagedValue = "value-staged-before-edit";
    private const string EditedValue = "value-edited-during-job";
    private const string CreatedName = "Created during the job";
    private const string CreatedValue = "value-created-during-job";

    [Fact]
    public async Task OwnerEdit_DuringStaging_TargetGetsTheLatestValue()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("ITransferStagingStore.StageSecretsAsync", 1, afterEffect: true);
        UseHost(CrashingTransferHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Edited during staging", StagedValue);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();
        await Runner.RunOnceAsync(Token);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(plan.Fired, "The staging write was never reached.");
        await ReplaceValueAsync(TestUsers.User, secret.Id, EditedValue);

        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Equal(EditedValue, await RevealAsync(TestUsers.OtherUser, secret.Id, RevealFieldDto.Value));
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task OwnerEdit_BeforeTheFirstReleasePack_TargetGetsTheLatestValue()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Func<Task> editDuringRelease = () => Task.CompletedTask;
        UseHost(RaceHost(clock, BeforeFirstReleasePack(() => editDuringRelease()), null));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Edited before release", StagedValue);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        editDuringRelease = () => ReplaceValueAsync(TestUsers.User, secret.Id, EditedValue);

        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Equal(EditedValue, await RevealAsync(TestUsers.OtherUser, secret.Id, RevealFieldDto.Value));
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task OwnerEdit_AfterRelease_ReturnsNotFoundOrConflictAndNothingIsLost()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Edited after release", StagedValue);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var body = new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Late edit", ReplaceValue = true, Value = EditedValue };
        var (response, _) = await ApiAs(TestUsers.User).UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, body);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict, $"Late edit returned {response.StatusCode}.");
        Assert.Equal(StagedValue, await RevealAsync(TestUsers.OtherUser, secret.Id, RevealFieldDto.Value));
    }

    [Fact]
    public async Task OwnerCreate_DuringStaging_InScopeItem_IsTransferred()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("ITransferStagingStore.StageSecretsAsync", 1, afterEffect: true);
        UseHost(CrashingTransferHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await CreateSecretAsync(TestUsers.User, "Existing before the job", StagedValue);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();
        await Runner.RunOnceAsync(Token);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(plan.Fired, "The staging write was never reached.");
        var created = await CreateSecretAsync(TestUsers.User, CreatedName, CreatedValue);

        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Contains(created.Id, (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id));
        Assert.Equal(CreatedValue, await RevealAsync(TestUsers.OtherUser, created.Id, RevealFieldDto.Value));
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    private async Task<TransferDto> ResumeUntilCompletedAsync(Guid transferId, FakeTimeProvider clock)
    {
        var current = await GetTransferAsync(TestUsers.Admin, transferId);
        for (var run = 0; run < RunBound && current.State != TransferStateDto.Completed; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transferId);
        }

        return current;
    }

    private async Task<SecretDto> CreateSecretAsync(TestUser owner, string name, string value)
    {
        var body = new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = name, Value = value };
        var (response, secret) = await ApiAs(owner).CreateSecretAsync(body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return secret ?? throw new InvalidOperationException("The create response had no body.");
    }

    private async Task ReplaceValueAsync(TestUser owner, Guid secretId, string value)
    {
        var (_, current) = await ApiAs(owner).GetSecretAsync(secretId);
        Assert.NotNull(current);
        var body = new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = current.Name ?? string.Empty, ReplaceValue = true, Value = value };
        var (response, _) = await ApiAs(owner).UpdateSecretAsync(secretId, current.ConcurrencyToken, body);
        Assert.True(response.IsSuccessStatusCode, $"Edit failed with {response.StatusCode}.");
    }
}
