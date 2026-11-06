// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components.Rendering;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SharpVaultComponentBaseTests : MudTestContext
{
    [Fact]
    public async Task Dispose_Always_CancelsTheToken()
    {
        var cut = Render<ProbeComponent>();
        var component = cut.Instance;

        await component.DisposeAsync();

        Assert.True(component.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_Twice_DoesNothing()
    {
        var resource = new CountingDisposable();
        var cut = Render<ProbeComponent>(parameters => parameters.Add(probe => probe.Resource, resource));

        await cut.Instance.DisposeAsync();
        await cut.Instance.DisposeAsync();

        Assert.Equal(1, resource.DisposeCount);
    }

    [Fact]
    public async Task Track_Disposable_IsDisposedWithTheComponent()
    {
        var resource = new CountingDisposable();
        var cut = Render<ProbeComponent>(parameters => parameters.Add(probe => probe.Resource, resource));
        Assert.Equal(0, resource.DisposeCount);

        await cut.Instance.DisposeAsync();

        Assert.Equal(1, resource.DisposeCount);
    }

    [Fact]
    public async Task Track_DisposableThatThrows_DoesNotStopTheOthers()
    {
        var thrower = new ThrowingDisposable();
        var resource = new CountingDisposable();
        var cut = Render<ProbeComponent>(parameters => parameters
            .Add(probe => probe.Resource, resource)
            .Add(probe => probe.Thrower, thrower));

        await cut.Instance.DisposeAsync();

        Assert.Equal(1, resource.DisposeCount);
        Assert.True(thrower.Attempted);
    }

    [Fact]
    public async Task StateChange_AfterDispose_DoesNotThrow()
    {
        var cut = Render<ProbeComponent>();
        await cut.Instance.DisposeAsync();

        var exception = await Record.ExceptionAsync(cut.Instance.ChangeStateAsync);

        Assert.Null(exception);
        Assert.True(cut.Instance.Disposed);
    }

    [Fact]
    public async Task Cts_IsNotDisposedWhenWorkStillRuns()
    {
        var cut = Render<ProbeComponent>();
        var token = cut.Instance.Token;
        await cut.Instance.DisposeAsync();

        var exception = Record.Exception(() =>
        {
            token.ThrowIfCancellationRequested();
        });

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Null(Record.Exception(() => cut.Instance.Token.WaitHandle));
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public bool Attempted { get; private set; }

        public void Dispose()
        {
            Attempted = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class ProbeComponent : SharpVaultComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        public IDisposable? Resource { get; set; }

        [Microsoft.AspNetCore.Components.Parameter]
        public IDisposable? Thrower { get; set; }

        public CancellationToken Token => Cts.Token;

        public bool Disposed => IsDisposed;

        public Task ChangeStateAsync() => InvokeStateChangedAsync();

        protected override void OnInitialized()
        {
            if (Thrower is not null)
            {
                Track(Thrower);
            }

            if (Resource is not null)
            {
                Track(Resource);
            }
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "probe");
    }
}
