// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor;
using MudBlazor.Services;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UnitTests.Support.Theme;

public class MudTestContext : BunitContext
{
    private static readonly TimeSpan DialogCloseTimeout = TimeSpan.FromSeconds(10);

    private readonly List<IDialogReference> _dialogReferences = [];

    private IRenderedComponent<MudPopoverProvider>? _popoverProvider;

    public MudTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<AntiforgeryStateProvider, StubAntiforgeryStateProvider>();
        Services.AddSingleton<ISessionActions, TestSessionActions>();
        Services.AddSingleton(new FakeTimeProvider());
        Services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<FakeTimeProvider>());
        Authorization = AddAuthorization();
    }

    protected BunitAuthorizationContext Authorization { get; }

    public IRenderedComponent<MudPopoverProvider> PopoverProvider =>
        _popoverProvider ?? throw new InvalidOperationException("The dialog providers have not been rendered.");

    public IRenderedComponent<MudDialogProvider> RenderDialogProviders()
    {
        if (Services.GetRequiredService<IDialogService>() is DialogService dialogService)
        {
            dialogService.DialogInstanceAddedAsync += RememberDialogAsync;
        }

        _popoverProvider ??= Render<MudPopoverProvider>();
        return Render<MudDialogProvider>();
    }

    public async Task WaitForClosedDialogsToSettleAsync()
    {
        IDialogReference[] references;
        lock (_dialogReferences)
        {
            references = [.. _dialogReferences];
        }

        foreach (var reference in references)
        {
            await reference.Result.WaitAsync(DialogCloseTimeout);
            await reference.Result.ContinueWith(static _ => { }, TaskScheduler.Default);
        }

        await WaitForThreadPoolQueueToDrainAsync();
        await Renderer.Dispatcher.InvokeAsync(static () => { });
    }

    private static async Task WaitForThreadPoolQueueToDrainAsync()
    {
        var started = TimeProvider.System.GetTimestamp();
        while (ThreadPool.PendingWorkItemCount > 0 && TimeProvider.System.GetElapsedTime(started) < DialogCloseTimeout)
        {
            await Task.Delay(1);
        }
    }

    private Task RememberDialogAsync(IDialogReference reference)
    {
        lock (_dialogReferences)
        {
            _dialogReferences.Add(reference);
        }

        return Task.CompletedTask;
    }
}
