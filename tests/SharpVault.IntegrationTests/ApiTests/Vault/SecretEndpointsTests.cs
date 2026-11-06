// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed class SecretEndpointsTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    [Fact]
    public async Task CreateSecret_AllFields_Returns201WithLocationAndNoValue()
    {
        var markers = new PlaintextMarkers();
        var body = new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            Username = markers.Username,
            Url = markers.Url,
            Tags = [markers.Tag],
            Value = markers.Value,
            Notes = markers.Notes,
        };
        var json = JsonSerializer.Serialize(body, VaultTestApi.Json);

        var response = await Api.PostRawAsync(ApiRoutes.VaultSecrets, json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Token);
        var created = JsonSerializer.Deserialize<SecretDto>(text, VaultTestApi.Json)!;
        Assert.EndsWith($"/api/vault/secrets/{created.Id}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(markers.Name, created.Name);
        Assert.Equal(markers.Username, created.Username);
        Assert.Equal(markers.Url, created.Url);
        Assert.Equal([markers.Tag], created.Tags);
        Assert.True(created.HasValue);
        Assert.True(created.HasNotes);
        Assert.False(string.IsNullOrWhiteSpace(created.ConcurrencyToken));
        Assert.DoesNotContain(markers.Value, text, StringComparison.Ordinal);
        Assert.DoesNotContain(markers.Notes, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSecret_ThenGet_ReturnsSameMetadata()
    {
        var created = await NewSecretAsync();

        var fetched = Require(await Api.GetSecretAsync(created.Id));

        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.Name, fetched.Name);
        Assert.Equal(created.Username, fetched.Username);
        Assert.Equal(created.Url, fetched.Url);
        Assert.Equal(created.Tags, fetched.Tags);
        Assert.Equal(created.Type, fetched.Type);
        Assert.Equal(created.HasValue, fetched.HasValue);
        Assert.Equal(created.HasNotes, fetched.HasNotes);
        Assert.Equal(created.CreatedAt, fetched.CreatedAt);
        Assert.Equal(created.ConcurrencyToken, fetched.ConcurrencyToken);
        Assert.False(fetched.IsDamaged);
    }

    [Fact]
    public async Task CreateSecret_SecureNoteWithoutValue_HasValueFalse()
    {
        var markers = new PlaintextMarkers();

        var created = Require(await Api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.SecureNote,
            Name = markers.Name,
            Notes = markers.Notes,
        }));

        Assert.False(created.HasValue);
        Assert.True(created.HasNotes);
        Assert.Equal(SecretTypeDto.SecureNote, created.Type);
        Assert.Equal(markers.Notes, await RevealNotesAsync(created.Id));
    }

    [Fact]
    public async Task ListSecrets_Empty_ReturnsEmptyPageWithoutToken()
    {
        var page = Require(await Api.ListSecretsAsync());

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListSecrets_OneItem_ReturnsItWithoutToken()
    {
        var created = await NewSecretAsync();

        var page = Require(await Api.ListSecretsAsync());

        var item = Assert.Single(page.Items);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal(created.Name, item.Name);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListSecrets_Exactly50_PageSize50_ReturnsAllAndNoFurtherItems()
    {
        var seeded = await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, 50, 0, Token);

        var first = Require(await Api.ListSecretsAsync(pageSize: 50));
        var rest = first.ContinuationToken is null
            ? new SecretPageDto()
            : Require(await Api.ListSecretsAsync(pageSize: 50, continuationToken: first.ContinuationToken));

        Assert.Equal(50, first.Items.Count);
        Assert.Equal(seeded.Secrets.Select(secret => secret.Id).Order(), first.Items.Select(item => item.Id).Order());
        Assert.Empty(rest.Items);
        Assert.Null(rest.ContinuationToken);
    }

    [Fact]
    public async Task ListSecrets_51_PageSize50_ReturnsTokenAndSecondPageHasOne()
    {
        var seeded = await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, 51, 0, Token);

        var first = Require(await Api.ListSecretsAsync(pageSize: 50));
        Assert.NotNull(first.ContinuationToken);
        var second = Require(await Api.ListSecretsAsync(pageSize: 50, continuationToken: first.ContinuationToken));

        Assert.Equal(50, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Null(second.ContinuationToken);
        var all = first.Items.Concat(second.Items).Select(item => item.Id).ToList();
        Assert.Equal(51, all.Distinct().Count());
        Assert.Equal(seeded.Secrets.Select(secret => secret.Id).Order(), all.Order());
    }

    [Fact]
    public async Task ListSecrets_DefaultOrder_IsCreationOrder()
    {
        var created = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            created.Add((await NewSecretAsync()).Id);
        }

        var listed = (await WholeListAsync()).Select(item => item.Id).ToList();

        Assert.Equal(created, listed);
    }

    [Fact]
    public async Task ListSecrets_FolderFilter_ReturnsOnlyThatFolder()
    {
        var folder = await NewFolderAsync("Filter");
        var other = await NewFolderAsync("Other");
        var inside = await NewSecretAsync(folder.Id);
        await NewSecretAsync(other.Id);
        await NewSecretAsync();

        var listed = await WholeListAsync(folderId: folder.Id);

        Assert.Equal(inside.Id, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task ListSecrets_Unfiled_ReturnsOnlyRootSecrets()
    {
        var folder = await NewFolderAsync("Filed");
        await NewSecretAsync(folder.Id);
        var root = await NewSecretAsync();

        var listed = await WholeListAsync(unfiled: true);

        var item = Assert.Single(listed);
        Assert.Equal(root.Id, item.Id);
        Assert.Null(item.FolderId);
    }

    [Fact]
    public async Task ListSecrets_Search_FindsByNameUsernameUrlAndTag()
    {
        var target = new PlaintextMarkers();
        var created = Require(await Api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = target.Name,
            Username = target.Username,
            Url = target.Url,
            Tags = [target.Tag],
            Value = target.Value,
        }));
        await NewSecretAsync();
        await NewSecretAsync();

        var byName = await WholeListAsync(search: target.Name);
        var byUsername = await WholeListAsync(search: target.Username.ToLowerInvariant());
        var byUrl = await WholeListAsync(search: target.Url[8..^1]);
        var byTag = await WholeListAsync(search: target.Tag.ToUpperInvariant());
        var byNothing = await WholeListAsync(search: $"absent-{Guid.NewGuid():N}");

        Assert.Equal(created.Id, Assert.Single(byName).Id);
        Assert.Equal(created.Id, Assert.Single(byUsername).Id);
        Assert.Equal(created.Id, Assert.Single(byUrl).Id);
        Assert.Equal(created.Id, Assert.Single(byTag).Id);
        Assert.Empty(byNothing);
    }

    [Fact]
    public async Task ListSecrets_SearchAcrossPages_ContinuesWithToken()
    {
        var common = $"common-{Guid.NewGuid():N}";
        var matching = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            var created = Require(await Api.CreateSecretAsync(new CreateSecretRequestDto
            {
                Type = SecretTypeDto.Login,
                Name = $"Match {index} {Guid.NewGuid():N}",
                Tags = [common],
                Value = "value",
            }));
            matching.Add(created.Id);
            await NewSecretAsync();
        }

        var found = new List<Guid>();
        var pages = 0;
        string? token = null;
        do
        {
            var page = Require(await Api.ListSecretsAsync(search: common, pageSize: 2, continuationToken: token));
            Assert.True(page.Items.Count <= 2);
            found.AddRange(page.Items.Select(item => item.Id));
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null && pages < 20);

        Assert.True(pages >= 3);
        Assert.Equal(matching, found);
    }

    [Fact]
    public async Task UpdateSecret_MetadataOnly_KeepsValueAndAddsNoVersion()
    {
        var markers = new PlaintextMarkers();
        var created = await CreateSecretAsync(Api, markers);
        var body = UpdateFrom(created);
        body.Name = $"{markers.Name}-renamed";
        body.Tags = [markers.Tag, "second"];

        var updated = Require(await Api.UpdateSecretAsync(created.Id, created.ConcurrencyToken, body));

        Assert.Equal($"{markers.Name}-renamed", updated.Name);
        Assert.Equal([markers.Tag, "second"], updated.Tags);
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);
        Assert.Equal(markers.Value, await RevealValueAsync(created.Id));
        Assert.Equal(markers.Notes, await RevealNotesAsync(created.Id));
        Assert.Empty(Require(await Api.ListHistoryAsync(created.Id)).Items);
    }

    [Fact]
    public async Task UpdateSecret_ReplaceValue_AddsVersionAndRevealsNewValue()
    {
        var markers = new PlaintextMarkers();
        var created = await CreateSecretAsync(Api, markers);
        var replacement = $"replacement-{Guid.NewGuid():N}";

        var updated = await ReplaceValueAsync(created, replacement);

        Assert.Equal(replacement, await RevealValueAsync(created.Id));
        var history = Require(await Api.ListHistoryAsync(created.Id));
        var version = Assert.Single(history.Items);
        Assert.Equal(markers.Value, await RevealVersionAsync(created.Id, version.VersionId));
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);
        Assert.Equal(markers.Notes, await RevealNotesAsync(created.Id));
    }

    [Fact]
    public async Task UpdateSecret_ClearNotes_HasNotesFalse()
    {
        var created = await NewSecretAsync();
        Assert.True(created.HasNotes);
        var body = UpdateFrom(created);
        body.ReplaceNotes = true;
        body.Notes = null;

        var updated = Require(await Api.UpdateSecretAsync(created.Id, created.ConcurrencyToken, body));

        Assert.False(updated.HasNotes);
        Assert.True(updated.HasValue);
        Assert.False(Require(await Api.GetSecretAsync(created.Id)).HasNotes);
        var reveal = await Api.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.View });
        Assert.Equal(HttpStatusCode.NotFound, reveal.Response.StatusCode);
    }

    [Fact]
    public async Task MoveSecret_BetweenFolders_UpdatesFolderIdOnly()
    {
        var first = await NewFolderAsync("First");
        var second = await NewFolderAsync("Second");
        var created = await NewSecretAsync(first.Id);

        var moved = Require(await Api.MoveSecretAsync(created.Id, created.ConcurrencyToken, new MoveSecretRequestDto { FolderId = second.Id }));

        Assert.Equal(second.Id, moved.FolderId);
        Assert.Equal(created.Name, moved.Name);
        Assert.Equal(created.Username, moved.Username);
        Assert.Equal(created.Url, moved.Url);
        Assert.Equal(created.Tags, moved.Tags);
        Assert.Equal(created.HasValue, moved.HasValue);
        Assert.Equal(created.HasNotes, moved.HasNotes);
        Assert.Equal(created.CreatedAt, moved.CreatedAt);
        Assert.Equal(second.Id, Require(await Api.GetSecretAsync(created.Id)).FolderId);
        Assert.Empty(Require(await Api.ListHistoryAsync(created.Id)).Items);
    }

    [Fact]
    public async Task MoveSecret_ToRoot_Succeeds()
    {
        var folder = await NewFolderAsync("Folder");
        var created = await NewSecretAsync(folder.Id);

        var moved = Require(await Api.MoveSecretAsync(created.Id, created.ConcurrencyToken, new MoveSecretRequestDto { FolderId = null }));

        Assert.Null(moved.FolderId);
        Assert.Null(Require(await Api.GetSecretAsync(created.Id)).FolderId);
        Assert.Equal(created.Id, Assert.Single(await WholeListAsync(unfiled: true)).Id);
    }

    [Fact]
    public async Task DeleteSecret_MovesToRecycleBin()
    {
        var created = await NewSecretAsync();

        var deleted = await Api.DeleteSecretAsync(created.Id, created.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Api.GetSecretAsync(created.Id)).Response.StatusCode);
        Assert.Empty(await WholeListAsync());
        var item = Assert.Single(await WholeBinAsync());
        Assert.Equal(RecycleBinItemKindDto.Secret, item.Kind);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal(created.Name, item.Name);
    }

    [Theory]
    [InlineData("plain value")]
    [InlineData("  leading and trailing  ")]
    [InlineData("line one\r\nline two\ttabbed")]
    [InlineData("café 😀 €")]
    [InlineData("é decomposed")]
    public async Task RevealValue_ReturnsExactValue(string value)
    {
        var created = Require(await Api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = "exact value",
            Value = value,
        }));

        var revealed = await RevealValueAsync(created.Id);

        Assert.Equal(value, revealed);
    }

    [Fact]
    public async Task RevealNotes_ReturnsExactNotes()
    {
        var notes = "  first line\r\nsecond line 😀  ";
        var created = Require(await Api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.SecureNote,
            Name = "exact notes",
            Notes = notes,
        }));

        var revealed = await RevealNotesAsync(created.Id);

        Assert.Equal(notes, revealed);
    }

    [Fact]
    public async Task RevealValue_SecretWithoutValue_Returns404()
    {
        var created = Require(await Api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.SecureNote,
            Name = "no value",
            Notes = "notes only",
        }));

        var response = await Api.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.NotFound, response.Response.StatusCode);
    }
}
