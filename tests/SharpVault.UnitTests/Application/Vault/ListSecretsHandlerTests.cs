// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Secrets.Queries;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class ListSecretsHandlerTests : VaultHandlerTestBase
{
    private ListSecretsQueryHandler Handler() => new(Store, KeyRings, Current);

    private static ListSecretsQuery Query(string? search = null, int pageSize = 50, string? token = null, Guid? folderId = null, bool unfiled = false) =>
        new(folderId, unfiled, search, pageSize, token);

    private List<Versioned<Secret>> Rows(params Secret[] secrets) => [.. secrets.Select(secret => new Versioned<Secret>(secret, NextETag()))];

    private void GivenPage(string? token, params Secret[] secrets) =>
        Store.ListSecretsAsync(Owner, Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SecretPage(Rows(secrets), token)));

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsEmptyWithoutSecretQuery()
    {
        GivenNoKeyRing();

        var result = await Handler().Handle(Query(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Null(result.Value.ContinuationToken);
        await Store.DidNotReceive().ListSecretsAsync(Arg.Any<Guid>(), Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PageWithToken_ReturnsLastIdAsToken()
    {
        var secrets = new[] { SecretWith("A"), SecretWith("B") };
        var token = ContinuationTokens.ForSecret(secrets[1].Id);
        GivenPage(token, secrets);

        var result = await Handler().Handle(Query(pageSize: 2), TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B"], result.Value!.Items.Select(item => item.Name));
        Assert.Equal(token, result.Value.ContinuationToken);
        await Store.Received(1).ListSecretsAsync(Owner, SecretFolderFilter.All, 2, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LastPage_ReturnsNullToken()
    {
        GivenPage(null, SecretWith("A"));

        var result = await Handler().Handle(Query(), TestContext.Current.CancellationToken);

        Assert.Single(result.Value!.Items);
        Assert.Null(result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_EmptyVault_ReturnsEmptyPage()
    {
        GivenPage(null);

        var result = await Handler().Handle(Query(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Value!.Items);
        Assert.Null(result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_ContinuationToken_IsPassedToTheStore()
    {
        GivenPage(null);
        var token = ContinuationTokens.ForSecret(Guid.NewGuid());

        await Handler().Handle(Query(token: token), TestContext.Current.CancellationToken);

        await Store.Received(1).ListSecretsAsync(Owner, Arg.Any<SecretFolderFilter>(), 50, token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FolderId_FiltersToThatFolder()
    {
        GivenPage(null);
        var folder = Guid.NewGuid();

        await Handler().Handle(Query(folderId: folder), TestContext.Current.CancellationToken);

        await Store.Received(1).ListSecretsAsync(Owner, SecretFolderFilter.InFolder(folder), 50, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Unfiled_FiltersToTheRoot()
    {
        GivenPage(null);

        await Handler().Handle(Query(unfiled: true), TestContext.Current.CancellationToken);

        await Store.Received(1).ListSecretsAsync(Owner, SecretFolderFilter.Unfiled, 50, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AlwaysQueriesTheCurrentUsersPartition()
    {
        GivenPage(null);

        await Handler().Handle(Query(search: "x"), TestContext.Current.CancellationToken);

        await Store.DidNotReceive().ListSecretsAsync(Arg.Is<Guid>(id => id != Owner), Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.NotEmpty(Store.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_Search_MatchesNameUsernameUrlAndTagCaseInsensitive()
    {
        GivenPage(
            null,
            SecretWith("Quarterly REPORT"),
            SecretWith("other", "Report-Admin"),
            SecretWith("another", null, "https://REPORT.test/"),
            SecretWith("tagged", null, null, "Reports"),
            SecretWith("unrelated", "someone", "https://example.test/", "misc"));

        var result = await Handler().Handle(Query(search: "report"), TestContext.Current.CancellationToken);

        Assert.Equal(["Quarterly REPORT", "other", "another", "tagged"], result.Value!.Items.Select(item => item.Name));
        Assert.Null(result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_Search_TrimsTheTermAndNeverReachesTheStore()
    {
        GivenPage(null, SecretWith("GitHub"), SecretWith("GitLab"));

        var result = await Handler().Handle(Query(search: "  github  "), TestContext.Current.CancellationToken);

        Assert.Equal(["GitHub"], result.Value!.Items.Select(item => item.Name));
        var calls = Store.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IVaultStore.ListSecretsAsync)).ToList();
        Assert.All(calls, call => Assert.DoesNotContain(call.GetArguments(), argument => argument is string text && text.Contains("github", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SearchWithODataSyntax_IsNeverSentToStorage()
    {
        GivenPage(null, SecretWith("plain"));

        var result = await Handler().Handle(Query(search: "' or PartitionKey ne '"), TestContext.Current.CancellationToken);

        Assert.Empty(result.Value!.Items);
        var arguments = Store.ReceivedCalls().SelectMany(call => call.GetArguments());
        Assert.DoesNotContain(arguments, argument => argument is string text && text.Contains("PartitionKey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_Search_StopsAtPageSizeAndReturnsLastExaminedId()
    {
        var secrets = Enumerable.Range(0, 6).Select(i => SecretWith("match-" + i)).ToArray();
        GivenPage("storage-next", secrets);

        var result = await Handler().Handle(Query(search: "match", pageSize: 3), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Value!.Items.Count);
        Assert.Equal(ContinuationTokens.ForSecret(secrets[2].Id), result.Value.ContinuationToken);
        Assert.Equal(["match-0", "match-1", "match-2"], result.Value.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Handle_Search_PageFilledByTheLastRowOfAStoragePage_ReturnsTheStorageToken()
    {
        var secrets = new[] { SecretWith("match-0"), SecretWith("skip"), SecretWith("match-1") };
        GivenPage("storage-next", secrets);

        var result = await Handler().Handle(Query(search: "match", pageSize: 2), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal("storage-next", result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_Search_PageFilledByTheLastRowOfTheVault_ReturnsNullToken()
    {
        GivenPage(null, SecretWith("match-0"), SecretWith("match-1"));

        var result = await Handler().Handle(Query(search: "match", pageSize: 2), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Null(result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_Search_UsesLargeScanBatchesAndPassesTheIncomingToken()
    {
        GivenPage(null, SecretWith("a"));
        var token = ContinuationTokens.ForSecret(Guid.NewGuid());

        await Handler().Handle(Query(search: "a", pageSize: 5, token: token), TestContext.Current.CancellationToken);

        await Store.Received(1).ListSecretsAsync(Owner, Arg.Any<SecretFolderFilter>(), VaultLimits.SearchScanBatchSize, token, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Search_StopsAt5000Scanned()
    {
        var calls = 0;
        Store.ListSecretsAsync(Owner, Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                var rows = Rows([.. Enumerable.Range(0, VaultLimits.SearchScanBatchSize).Select(i => SecretWith("noise-" + calls + "-" + i))]);
                return Task.FromResult(new SecretPage(rows, "storage-" + calls));
            });

        var result = await Handler().Handle(Query(search: "needle"), TestContext.Current.CancellationToken);

        Assert.Equal(VaultLimits.SearchMaxScannedRows / VaultLimits.SearchScanBatchSize, calls);
        Assert.Empty(result.Value!.Items);
        Assert.Equal("storage-" + calls, result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_Search_ScanEndsWhenTheVaultEnds()
    {
        var calls = 0;
        Store.ListSecretsAsync(Owner, Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                var rows = Rows(SecretWith("noise"));
                return Task.FromResult(new SecretPage(rows, calls < 3 ? "more" : null));
            });

        var result = await Handler().Handle(Query(search: "needle"), TestContext.Current.CancellationToken);

        Assert.Equal(3, calls);
        Assert.Empty(result.Value!.Items);
        Assert.Null(result.Value.ContinuationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DamagedRow_FlaggedNotFailed()
    {
        GivenPage(null, SecretWith("fine"), DamagedSecret());

        var result = await Handler().Handle(Query(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.False(result.Value.Items[0].IsDamaged);
        var damaged = result.Value.Items[1];
        Assert.True(damaged.IsDamaged);
        Assert.Null(damaged.Name);
        Assert.Null(damaged.Username);
    }

    [Fact]
    public async Task Handle_Search_DamagedRowIsSkippedNotMatchedOrFailed()
    {
        GivenPage(null, DamagedSecret(), SecretWith("match"));

        var result = await Handler().Handle(Query(search: "match"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["match"], result.Value!.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Handle_OpensMetadataSessionOncePerRequest()
    {
        GivenPage(null, [.. Enumerable.Range(0, 50).Select(i => SecretWith("s" + i))]);
        var baseline = Harness.Provider.UnwrapCount;

        var result = await Handler().Handle(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(50, result.Value!.Items.Count);
        Assert.Equal(baseline + 1, Harness.Provider.UnwrapCount);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Items_NeverCarryValueOrNotes()
    {
        var secret = SecretWith("A");
        var value = DecryptCurrentValue(secret);
        GivenPage(null, secret);

        var result = await Handler().Handle(Query(), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(value, System.Text.Json.JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
        Assert.True(result.Value!.Items[0].HasValue);
    }

    [Fact]
    public async Task Handle_UnreadableKeyRing_ListsEveryRowAsDamaged()
    {
        GivenPage("next", SecretWith("One"), SecretWith("Two"));
        GivenUnreadableKeyRing();

        var result = await Handler().Handle(Query(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, item =>
        {
            Assert.True(item.IsDamaged);
            Assert.Null(item.Name);
        });
        Assert.Equal("next", result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_UnreadableKeyRingWithSearch_ReturnsAnEmptyPage()
    {
        GivenPage(null, SecretWith("One"));
        GivenUnreadableKeyRing();

        var result = await Handler().Handle(Query(search: "one"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("needle")]
    public async Task Handle_StoreRejectsTheContinuationToken_ReturnsRejectedInvalidToken(string? search)
    {
        Store.ListSecretsAsync(Owner, Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<SecretPage>(_ => throw new InvalidContinuationTokenException());

        var result = await Handler().Handle(Query(search: search, token: ContinuationTokens.ForStorage("forged")), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.InvalidContinuationToken, result.Error);
    }
}
