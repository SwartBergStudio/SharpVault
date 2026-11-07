// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.JSInterop;
using SharpVault.UI.Shared.Components;

namespace SharpVault.UI.Shared.Services.OneTimeLinks;

public sealed class BrowserOneTimeLinkCrypto(IJSRuntime jsRuntime) : IOneTimeLinkCrypto, IAsyncDisposable
{
    public const string ModulePath = "./_content/SharpVault.UI.Shared/js/one-time-link-create.js";

    private const string SealedStatus = "sealed";
    private const string PassphraseTooShortStatus = "passphrase-too-short";
    private const string UnsupportedStatus = "unsupported";
    private const string SealFromPageFunction = "sealFromPage";
    private const string PayloadFunction = "payload";
    private const string PublishFunction = "publish";
    private const string CopyLinkFunction = "copyLink";
    private const string DiscardFunction = "discard";

    private IJSObjectReference? _module;

    public async Task<OneTimeLinkSeal> SealAsync(string snapshotJson, string passphraseInputId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        ComponentIds.EnsureValid(passphraseInputId, nameof(passphraseInputId));

        var module = await ModuleAsync(cancellationToken);
        var result = await module.InvokeAsync<OneTimeLinkSealResponse>(SealFromPageFunction, cancellationToken, snapshotJson, passphraseInputId);
        return result.Status switch
        {
            SealedStatus when result.Handle is not null => await ReadSealAsync(module, result.Handle, cancellationToken),
            PassphraseTooShortStatus => new OneTimeLinkSeal(OneTimeLinkSealStatus.PassphraseTooShort, null, null),
            UnsupportedStatus => new OneTimeLinkSeal(OneTimeLinkSealStatus.Unsupported, null, null),
            _ => throw new InvalidOperationException("The browser returned an unknown seal result."),
        };
    }

    public async Task PublishAsync(string handle, Guid linkId, string baseUri, string outputInputId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUri);
        ComponentIds.EnsureValid(outputInputId, nameof(outputInputId));

        var module = await ModuleAsync(cancellationToken);
        await module.InvokeVoidAsync(PublishFunction, cancellationToken, handle, baseUri, linkId.ToString("D"), outputInputId);
    }

    public async Task<bool> CopyLinkAsync(string outputInputId, CancellationToken cancellationToken)
    {
        ComponentIds.EnsureValid(outputInputId, nameof(outputInputId));

        var module = await ModuleAsync(cancellationToken);
        return await module.InvokeAsync<bool>(CopyLinkFunction, cancellationToken, outputInputId);
    }

    public async Task DiscardAsync(string handle, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        var module = await ModuleAsync(cancellationToken);
        await module.InvokeVoidAsync(DiscardFunction, cancellationToken, handle);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }

        _module = null;
    }

    private static async Task<OneTimeLinkSeal> ReadSealAsync(IJSObjectReference module, string handle, CancellationToken cancellationToken)
    {
        await using var reference = await module.InvokeAsync<IJSStreamReference>(PayloadFunction, cancellationToken, handle);
        await using var stream = await reference.OpenReadStreamAsync(OneTimeLinkUiLimits.MaxPayloadBytes, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return new OneTimeLinkSeal(OneTimeLinkSealStatus.Sealed, handle, buffer.ToArray());
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken) =>
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
}
