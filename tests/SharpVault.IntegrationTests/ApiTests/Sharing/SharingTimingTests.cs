// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Net;
using SharpVault.Dtos.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(PerformanceTestGroup))]
[Trait("Category", "Performance")]
public sealed class SharingTimingTests(ApiTestFactory factory) : SharingSecurityTestBase(factory)
{
    private const int WarmIterations = 3;
    private const int MeasuredIterations = 9;

    private static readonly TimeSpan DirectBudget = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WriteBudget = TimeSpan.FromMilliseconds(300);

    private static async Task<TimeSpan> MedianAsync(Func<Task> action)
    {
        for (var warm = 0; warm < WarmIterations; warm++)
        {
            await action();
        }

        var samples = new List<TimeSpan>();
        for (var run = 0; run < MeasuredIterations; run++)
        {
            var clock = Stopwatch.StartNew();
            await action();
            clock.Stop();
            samples.Add(clock.Elapsed);
        }

        return samples.Order().ElementAt(samples.Count / 2);
    }

    private static void AssertWithin(TimeSpan median, TimeSpan budget, string name) =>
        Assert.True(median <= budget, $"{name} took a median of {median.TotalMilliseconds:F0} ms against a budget of {budget.TotalMilliseconds:F0} ms.");

    [Fact]
    public async Task SharedReveal_Direct_Under200ms()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        var median = await MedianAsync(async () => Assert.Equal(HttpStatusCode.OK, await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id)));

        AssertWithin(median, DirectBudget, "A direct shared reveal");
    }

    [Fact]
    public async Task SharedReveal_FolderChainOfTen_Under300ms()
    {
        var current = await NewFolderAsync("chain-1");
        var top = current.Id;
        for (var level = 2; level <= 10; level++)
        {
            current = await NewFolderAsync($"chain-{level}", current.Id);
        }

        var secret = await NewSecretAsync(new PlaintextMarkers(), current.Id);
        await ShareFolderAsync(top, TestUsers.FolderViewer, SharePermissionDto.View);

        var median = await MedianAsync(async () => Assert.Equal(HttpStatusCode.OK, await SharedRevealStatusAsync(TestUsers.FolderViewer, secret.Id)));

        AssertWithin(median, WriteBudget, "A shared reveal through a chain of ten folders");
    }

    [Fact]
    public async Task CreateGrantThenRevoke_EachUnder300ms()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var owner = SharingFor(Owner);
        await WarmUpAsync(Owner);
        var created = new List<TimeSpan>();
        var revoked = new List<TimeSpan>();

        for (var run = 0; run < MeasuredIterations; run++)
        {
            var clock = Stopwatch.StartNew();
            var create = (await owner.CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response;
            clock.Stop();
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            created.Add(clock.Elapsed);

            clock.Restart();
            var revoke = await owner.RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);
            clock.Stop();
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            revoked.Add(clock.Elapsed);
        }

        AssertWithin(created.Order().ElementAt(created.Count / 2), WriteBudget, "Creating a grant");
        AssertWithin(revoked.Order().ElementAt(revoked.Count / 2), WriteBudget, "Revoking a grant");
    }

    [Fact]
    public async Task SearchDirectory_Under300ms()
    {
        await WarmUpAsync(Owner);

        var median = await MedianAsync(async () => Assert.Equal(HttpStatusCode.OK, (await SharingFor(Owner).SearchDirectoryAsync("sharpvault")).Response.StatusCode));

        AssertWithin(median, WriteBudget, "A directory search");
    }

    [Fact]
    public async Task ListSharedItems_PageOfTwentyFromTwoOwners_Under500ms()
    {
        foreach (var owner in new[] { Owner, TestUsers.OtherUser })
        {
            for (var index = 0; index < 10; index++)
            {
                var secret = await NewSecretAsync(new PlaintextMarkers(), owner: owner);
                await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View, by: owner, ownerId: owner.ObjectId);
            }
        }

        var median = await MedianAsync(async () => Assert.Equal(20, (await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync()).Body!.Items.Count));

        AssertWithin(median, TimeSpan.FromMilliseconds(500), "Listing a shared page");
    }
}
