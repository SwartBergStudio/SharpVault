// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using SharpVault.UI.Shared.Services.Idle;
using IdleMonitorType = SharpVault.UI.Shared.Services.Idle.IdleMonitor;

namespace SharpVault.UnitTests.UiShared.Services;

[Trait("Category", "Security")]
public sealed class IdleMonitorTests : BunitContext
{
    private const string SignOutFormId = "sign-out-form";
    private const string SessionTag = "1893456000";
    private const string StartIdentifier = "start";
    private const string StopIdentifier = "stop";
    private const string ImportIdentifier = "import";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    public IdleMonitorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule(IdleMonitorType.ModulePath);
    }

    private IdleMonitorType NewMonitor() => new(Services.GetRequiredService<IJSRuntime>());

    private DotNetObjectReference<IdleMonitorType> LastStartReference()
    {
        var start = JSInterop.Invocations.Last(invocation => invocation.Identifier == StartIdentifier);
        return Assert.IsType<DotNetObjectReference<IdleMonitorType>>(start.Arguments[^1]);
    }

    [Fact]
    public async Task IdleMonitor_Heartbeat_IsInvokableFromJs()
    {
        var monitor = NewMonitor();

        await monitor.StartAsync(SignOutFormId, Timeout, SessionTag, Xunit.TestContext.Current.CancellationToken);

        Assert.Same(monitor, LastStartReference().Value);
        var heartbeat = typeof(IdleMonitorType).GetMethod(nameof(IdleMonitorType.Heartbeat), BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(heartbeat?.GetCustomAttribute<JSInvokableAttribute>());
    }

    [Fact]
    public async Task IdleMonitor_Dispose_DisposesTheReference()
    {
        var monitor = NewMonitor();
        await monitor.StartAsync(SignOutFormId, Timeout, SessionTag, Xunit.TestContext.Current.CancellationToken);
        var first = LastStartReference();

        await monitor.DisposeAsync();

        Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == StopIdentifier);
        await monitor.StartAsync(SignOutFormId, Timeout, SessionTag, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(2, JSInterop.Invocations.Count(invocation => invocation.Identifier == ImportIdentifier));
        Assert.NotSame(first, LastStartReference());
        await monitor.DisposeAsync();
    }
}
