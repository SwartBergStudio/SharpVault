// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
public sealed class DirectoryTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private const string SharePrefix = "share";

    [Fact]
    public async Task DirectoryExcludesCaller()
    {
        var caller = SharingFor(TestUsers.ShareViewer);

        var found = Require(await caller.SearchDirectoryAsync(SharePrefix));

        Assert.DoesNotContain(found.Items, item => item.Id == TestUsers.ShareViewer.ObjectId);
        Assert.Contains(found.Items, item => item.Id == TestUsers.ShareEditor.ObjectId);
        Assert.Contains(found.Items, item => item.Id == TestUsers.ShareManager.ObjectId);
    }

    [Fact]
    public async Task SearchDirectory_ByEmail_ReturnsIdNameAndEmail()
    {
        var result = await OwnerSharing.SearchDirectoryAsync("share-editor@");

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        var match = Assert.Single(result.Body!.Items);
        Assert.Equal(TestUsers.ShareEditor.ObjectId, match.Id);
        Assert.Equal(TestUsers.ShareEditor.DisplayName, match.DisplayName);
        Assert.Equal(TestUsers.ShareEditor.Email, match.Email);
    }

    [Fact]
    public async Task SearchDirectory_NoMatch_ReturnsEmptyList()
    {
        var result = Require(await OwnerSharing.SearchDirectoryAsync("zzzzqqqq"));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchDirectory_LockedUser_NotReturned()
    {
        await WarmUpAsync(TestUsers.LockedRecipient);
        await LockUserAsync(TestUsers.LockedRecipient.ObjectId);

        var result = Require(await OwnerSharing.SearchDirectoryAsync("locked recipient"));

        Assert.DoesNotContain(result.Items, item => item.Id == TestUsers.LockedRecipient.ObjectId);
    }
}
