// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Clipboard;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.Support.Vault;

public class VaultTestContext : ShellTestContext
{
    public VaultTestContext()
    {
        Clipboard = new FakeClipboard();
        Settings = VaultUiSettings.Default;
        SecretsClient = Substitute.For<IVaultSecretsApiClient>();
        HistoryClient = Substitute.For<ISecretHistoryApiClient>();
        FoldersClient = Substitute.For<IVaultFoldersApiClient>();
        RecycleBinClient = Substitute.For<IRecycleBinApiClient>();
        GrantsClient = Substitute.For<IShareGrantsApiClient>();
        DirectoryClient = Substitute.For<IDirectoryApiClient>();
        SharedClient = Substitute.For<ISharedItemsApiClient>();
        SharedClient.ListAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto())));
        Services.AddSingleton(SharedClient);
        Services.AddSingleton(SecretsClient);
        Services.AddSingleton(HistoryClient);
        Services.AddSingleton(FoldersClient);
        Services.AddSingleton(RecycleBinClient);
        Services.AddSingleton(GrantsClient);
        Services.AddSingleton(DirectoryClient);
        Services.AddSingleton<IClipboard>(Clipboard);
        Services.AddSingleton(Settings);
        Services.AddSingleton<SecretClipboard>();
    }

    internal FakeClipboard Clipboard { get; }

    public VaultUiSettings Settings { get; }

    public IVaultSecretsApiClient SecretsClient { get; }

    public ISecretHistoryApiClient HistoryClient { get; }

    public IVaultFoldersApiClient FoldersClient { get; }

    public IRecycleBinApiClient RecycleBinClient { get; }

    public IShareGrantsApiClient GrantsClient { get; }

    public IDirectoryApiClient DirectoryClient { get; }

    public ISharedItemsApiClient SharedClient { get; }

    public SecretClipboard SecretClipboard => Services.GetRequiredService<SecretClipboard>();

    public IElement OpenFolderAction<TComponent>(IRenderedComponent<TComponent> rendered, Guid folderId, string action)
        where TComponent : IComponent
    {
        rendered.WaitForElement($"#folder-actions-{folderId:D} button").Click();
        return PopoverProvider.WaitForElement($"#folder-{action}-{folderId:D}");
    }
}
