// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

[Trait("Category", "Security")]
public sealed class VaultMarkupTests
{
    private const string InteractiveSelector = "button, a, input, select, textarea";

    private static readonly Guid FolderId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ChildFolderId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private static readonly Dictionary<string, Func<VaultTestContext, Task<IReadOnlyList<IElement>>>> Scenarios = new()
    {
        ["vault-browser"] = VaultBrowserAsync,
        ["folder-tree-editor-dialog"] = FolderEditorDialogAsync,
        ["folder-tree-delete-dialog"] = FolderDeleteDialogAsync,
        ["secret-detail"] = SecretDetailAsync,
        ["secret-detail-delete-dialog"] = SecretDetailDeleteDialogAsync,
        ["secret-detail-move-dialog"] = SecretDetailMoveDialogAsync,
        ["secret-new"] = SecretNewAsync,
        ["secret-new-generator-dialog"] = SecretGeneratorDialogAsync,
        ["secret-edit"] = SecretEditAsync,
        ["secret-history"] = SecretHistoryAsync,
        ["secret-history-restore-dialog"] = SecretHistoryRestoreDialogAsync,
        ["recycle-bin"] = RecycleBinAsync,
        ["recycle-bin-purge-dialog"] = RecycleBinPurgeDialogAsync,
    };

    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Scenarios.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    private static SecretDto Secret(string name, bool damaged = false) => new()
    {
        Id = Guid.NewGuid(),
        Type = SecretTypeDto.Login,
        Name = name,
        Username = "user",
        HasValue = true,
        HasNotes = true,
        Tags = ["alpha"],
        IsDamaged = damaged,
        ConcurrencyToken = "etag-" + name,
    };

    private static void Seed(VaultTestContext context, SecretDto secret)
    {
        context.SecretsClient.GetSecretAsync(secret.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretDto>.Success(secret)));
        context.SecretsClient.ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(ApiResponse<SecretPageDto>.Success(call.ArgAt<string?>(4) is null
                ? new SecretPageDto { Items = [secret, Secret("Damaged", damaged: true)], ContinuationToken = "more" }
                : new SecretPageDto { Items = [Secret("Second")] })));
        context.FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto
            {
                Items =
                [
                    new FolderDto { Id = FolderId, Name = "Finance" },
                    new FolderDto { Id = ChildFolderId, ParentId = FolderId, Name = "Payroll" },
                ],
            })));
    }

    private static List<IElement> Everything(params IRenderedComponent<IComponent>[] fragments) =>
        fragments.SelectMany(fragment => fragment.FindAll("*")).ToList();

    private static async Task<IReadOnlyList<IElement>> VaultBrowserAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var secret = Secret("Router");
        Seed(context, secret);
        var cut = context.Render<VaultBrowser>();
        cut.WaitForElement($"#vault-secret-link-{secret.Id:D}");
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("[id^=vault-secret-link-]").Count >= 2));
        context.OpenFolderAction(cut, FolderId, "delete");
        await Task.CompletedTask;
        return Everything(cut, provider, context.PopoverProvider);
    }

    private static async Task<IReadOnlyList<IElement>> FolderEditorDialogAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        Seed(context, Secret("Router"));
        var cut = context.Render<FolderTreePanel>();
        cut.WaitForElement("#vault-new-folder");
        await cut.Find("#vault-new-folder").ClickAsync();
        provider.WaitForElement("#folder-editor-save");
        return Everything(cut, provider);
    }

    private static async Task<IReadOnlyList<IElement>> FolderDeleteDialogAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        Seed(context, Secret("Router"));
        var cut = context.Render<FolderTreePanel>();
        var deleteItem = context.OpenFolderAction(cut, FolderId, "delete");
        await deleteItem.ClickAsync();
        provider.WaitForElement("#folder-delete-confirm");
        return Everything(cut, provider, context.PopoverProvider);
    }

    private static async Task<IReadOnlyList<IElement>> SecretDetailAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var secret = Secret("Router");
        Seed(context, secret);
        var cut = context.Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, secret.Id));
        cut.WaitForElement("#secret-heading");
        await Task.CompletedTask;
        return Everything(cut, provider);
    }

    private static async Task<IReadOnlyList<IElement>> SecretDetailDeleteDialogAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var secret = Secret("Router");
        Seed(context, secret);
        var cut = context.Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, secret.Id));
        cut.WaitForElement("#secret-heading");
        await cut.Find("#secret-delete").ClickAsync();
        provider.WaitForElement("#secret-delete-prompt-confirm");
        return Everything(cut, provider);
    }

    private static async Task<IReadOnlyList<IElement>> SecretDetailMoveDialogAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var secret = Secret("Router");
        Seed(context, secret);
        var cut = context.Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, secret.Id));
        cut.WaitForElement("#secret-heading");
        await cut.Find("#secret-move").ClickAsync();
        provider.WaitForElement("#move-secret-confirm");
        return Everything(cut, provider);
    }

    private static async Task<IReadOnlyList<IElement>> SecretNewAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        Seed(context, Secret("Router"));
        var cut = context.Render<SecretEditor>();
        cut.WaitForElement("#secret-save");
        await Task.CompletedTask;
        return Everything(cut, provider);
    }

    private static async Task<IReadOnlyList<IElement>> SecretGeneratorDialogAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        Seed(context, Secret("Router"));
        var cut = context.Render<SecretEditor>();
        cut.WaitForElement("#secret-generate");
        await cut.Find("#secret-generate").ClickAsync();
        provider.WaitForElement("#generator-output");
        return Everything(cut, provider);
    }

    private static async Task<IReadOnlyList<IElement>> SecretEditAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var secret = Secret("Router");
        Seed(context, secret);
        var cut = context.Render<SecretEditor>(parameters => parameters.Add(editor => editor.SecretId, secret.Id));
        cut.WaitForElement("#secret-save");
        await Task.CompletedTask;
        return Everything(cut, provider);
    }

    private static SecretVersionDto Version() => new()
    {
        VersionId = Guid.NewGuid(),
        ReplacedAt = DateTimeOffset.UnixEpoch,
        Revision = 1,
    };

    private static IRenderedComponent<SecretHistoryView> History(VaultTestContext context, SecretVersionDto version)
    {
        var secret = Secret("Router");
        Seed(context, secret);
        context.HistoryClient.ListVersionsAsync(secret.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretVersionListDto>.Success(new SecretVersionListDto { Items = [version] })));
        var cut = context.Render<SecretHistoryView>(parameters => parameters.Add(view => view.SecretId, secret.Id));
        cut.WaitForElement($"#history-restore-{version.VersionId:D}");
        return cut;
    }

    private static async Task<IReadOnlyList<IElement>> SecretHistoryAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var cut = History(context, Version());
        await Task.CompletedTask;
        return Everything(cut, provider);
    }

    private static async Task<IReadOnlyList<IElement>> SecretHistoryRestoreDialogAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var version = Version();
        var cut = History(context, version);
        await cut.Find($"#history-restore-{version.VersionId:D}").ClickAsync();
        provider.WaitForElement("#history-restore-prompt-confirm");
        return Everything(cut, provider);
    }

    private static RecycleBinItemDto BinItem(RecycleBinItemKindDto kind, bool damaged = false) => new()
    {
        Kind = kind,
        Id = Guid.NewGuid(),
        Name = kind.ToString(),
        Type = kind == RecycleBinItemKindDto.Secret ? SecretTypeDto.Login : null,
        DeletionBatchId = Guid.NewGuid(),
        DeletedAt = DateTimeOffset.UnixEpoch,
        PurgeAfter = DateTimeOffset.UnixEpoch.AddDays(30),
        IsDamaged = damaged,
    };

    private static IRenderedComponent<RecycleBinView> Bin(VaultTestContext context, RecycleBinItemDto first)
    {
        context.RecycleBinClient.ListAsync(RecycleBinView.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<RecycleBinPageDto>.Success(new RecycleBinPageDto
            {
                Items = [first, BinItem(RecycleBinItemKindDto.Folder), BinItem(RecycleBinItemKindDto.Secret, damaged: true)],
                ContinuationToken = "more",
            })));
        var cut = context.Render<RecycleBinView>();
        cut.WaitForElement("#bin-load-more");
        return cut;
    }

    private static async Task<IReadOnlyList<IElement>> RecycleBinAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var cut = Bin(context, BinItem(RecycleBinItemKindDto.Secret));
        await Task.CompletedTask;
        return Everything(cut, provider);
    }

    private static async Task<IReadOnlyList<IElement>> RecycleBinPurgeDialogAsync(VaultTestContext context)
    {
        var provider = context.RenderDialogProviders();
        var item = BinItem(RecycleBinItemKindDto.Secret);
        var cut = Bin(context, item);
        await cut.Find($"#{RecycleBinItemText.IdFor("bin-purge", item)}").ClickAsync();
        provider.WaitForElement("#bin-purge-prompt-confirm");
        return Everything(cut, provider);
    }

    private static bool IsInteractive(IElement element) =>
        element.Matches(InteractiveSelector) && !IsHiddenInput(element);

    private static bool IsHiddenInput(IElement element) =>
        element.LocalName == "input" && string.Equals(element.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase);

    private static string Describe(IElement element) => element.OuterHtml.Length > 160 ? element.OuterHtml[..160] : element.OuterHtml;

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task AllVaultComponents_EveryInteractiveElement_HasId(string scenario)
    {
        await using var context = new VaultTestContext();

        var elements = await Scenarios[scenario](context);

        var interactive = elements.Where(IsInteractive).ToList();
        var missing = interactive.Where(element => string.IsNullOrWhiteSpace(element.Id)).Select(Describe).ToList();
        Assert.NotEmpty(interactive);
        Assert.True(missing.Count == 0, $"{scenario}: interactive elements without an id:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task AllVaultComponents_Ids_AreUniquePerRender(string scenario)
    {
        await using var context = new VaultTestContext();

        var elements = await Scenarios[scenario](context);

        var duplicates = elements
            .Where(element => !string.IsNullOrEmpty(element.Id))
            .GroupBy(element => element.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} x{group.Count()}")
            .ToList();
        Assert.True(duplicates.Count == 0, $"{scenario}: duplicate ids: {string.Join(", ", duplicates)}");
    }
}
