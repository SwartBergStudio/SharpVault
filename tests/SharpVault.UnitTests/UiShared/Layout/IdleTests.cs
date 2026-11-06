// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using SharpVault.UI.Shared.Components.Layout;
using SharpVault.UI.Shared.Services.Idle;
using SharpVault.UnitTests.Support.Theme;
using IdleMonitorType = SharpVault.UI.Shared.Services.Idle.IdleMonitor;

namespace SharpVault.UnitTests.UiShared.Layout;

public sealed class IdleTests : ShellTestContext
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    [Fact]
    public void IdleLock_SignedIn_StartsTheModuleWithTheFormAndTheConfiguredTimeout()
    {
        Authorization.SetAuthorized(DisplayName);

        Render<IdleLock>();

        IdleMonitor.Received(1).StartAsync("sign-out-form", DefaultTimeout, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IdleLock_CustomTimeout_IsPassedToTheModule()
    {
        Authorization.SetAuthorized(DisplayName);
        IdleOptions.Timeout = TimeSpan.FromMinutes(3);

        Render<IdleLock>();

        IdleMonitor.Received(1).StartAsync(Arg.Any<string>(), TimeSpan.FromMinutes(3), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IdleLock_SignedOut_NeverStartsTheModule()
    {
        Render<IdleLock>();

        _ = IdleMonitor.DidNotReceive().StartAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IdleLock_ModuleFailsToLoad_DoesNotThrowAndLogsAWarning()
    {
        Authorization.SetAuthorized(DisplayName);
        IdleMonitor.StartAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new JSException("The idle module was blocked")));

        Render<IdleLock>();

        Assert.Contains("The idle monitor could not start", LogMessages);
    }

    [Fact]
    public async Task IdleLock_Dispose_StopsTheModule()
    {
        Authorization.SetAuthorized(DisplayName);
        Render<IdleLock>();

        await DisposeComponentsAsync();

        _ = IdleMonitor.Received(1).StopAsync();
    }

    [Fact]
    public async Task IdleLock_DisposeWhenNeverStarted_DoesNotStopTheModule()
    {
        Render<IdleLock>();

        await DisposeComponentsAsync();

        _ = IdleMonitor.DidNotReceive().StopAsync();
    }

    [Fact]
    public async Task IdleLock_DisposedWhileStarting_StopsTheModuleAfterTheStartReturns()
    {
        Authorization.SetAuthorized(DisplayName);
        var gate = new TaskCompletionSource();
        IdleMonitor.StartAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<IdleLock>();
        cut.WaitForAssertion(() => IdleMonitor.Received(1).StartAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()));

        await DisposeComponentsAsync();
        gate.SetResult();

        await cut.WaitForAssertionAsync(() => IdleMonitor.Received(1).StopAsync());
    }

    [Fact]
    public async Task IdleLock_DisposedWhileReadingTheAuthenticationState_NeverStartsOrStopsTheModuleAndDoesNotThrow()
    {
        var gate = new TaskCompletionSource<AuthenticationState>();
        var provider = Substitute.For<AuthenticationStateProvider>();
        provider.GetAuthenticationStateAsync().Returns(gate.Task);
        Services.AddSingleton(provider);
        var cut = Render<IdleLock>();
        var afterRender = typeof(IdleLock).GetMethod("OnAfterRenderAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var pending = (Task)afterRender.Invoke(cut.Instance, [true])!;

        await DisposeComponentsAsync();
        gate.SetResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity("test"))));
        await pending;

        _ = IdleMonitor.DidNotReceive().StartAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _ = IdleMonitor.DidNotReceive().StopAsync();
    }

    [Fact]
    public async Task IdleMonitor_Start_ImportsTheModuleAndPassesTheFormIdAndMilliseconds()
    {
        var module = JSInterop.SetupModule(IdleMonitorType.ModulePath);
        module.SetupVoid("start", _ => true).SetVoidResult();
        await using var monitor = new IdleMonitorType(Services.GetRequiredService<IJSRuntime>());

        await monitor.StartAsync("sign-out-form", TimeSpan.FromMinutes(2), null, Xunit.TestContext.Current.CancellationToken);

        var invocation = Assert.Single(module.Invocations["start"]);
        Assert.Equal("sign-out-form", invocation.Arguments[0]);
        Assert.Equal(120000L, invocation.Arguments[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IdleMonitor_StartWithBlankFormId_Throws(string formId)
    {
        await using var monitor = new IdleMonitorType(Services.GetRequiredService<IJSRuntime>());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => monitor.StartAsync(formId, TimeSpan.FromMinutes(1), null, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdleMonitor_Stop_InvokesStop()
    {
        var module = JSInterop.SetupModule(IdleMonitorType.ModulePath);
        module.SetupVoid("start", _ => true).SetVoidResult();
        module.SetupVoid("stop", _ => true).SetVoidResult();
        await using var monitor = new IdleMonitorType(Services.GetRequiredService<IJSRuntime>());
        await monitor.StartAsync("sign-out-form", TimeSpan.FromMinutes(1), null, Xunit.TestContext.Current.CancellationToken);

        await monitor.StopAsync();

        Assert.Single(module.Invocations["stop"]);
    }

    [Fact]
    public async Task IdleMonitor_Dispose_StopsAndDisposesTheModule()
    {
        var module = JSInterop.SetupModule(IdleMonitorType.ModulePath);
        module.SetupVoid("start", _ => true).SetVoidResult();
        module.SetupVoid("stop", _ => true).SetVoidResult();
        var monitor = new IdleMonitorType(Services.GetRequiredService<IJSRuntime>());
        await monitor.StartAsync("sign-out-form", TimeSpan.FromMinutes(1), null, Xunit.TestContext.Current.CancellationToken);

        await monitor.DisposeAsync();
        await monitor.DisposeAsync();

        Assert.Single(module.Invocations["stop"]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(900, true)]
    [InlineData(28800, true)]
    [InlineData(28801, false)]
    public void IdleLockOptionsValidator_Rules(int seconds, bool valid)
    {
        var result = new IdleLockOptionsValidator().Validate(null, new IdleLockOptions { Timeout = TimeSpan.FromSeconds(seconds) });

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void IdleLockOptions_Default_IsFifteenMinutesAndValid()
    {
        var options = new IdleLockOptions();

        Assert.Equal(TimeSpan.FromMinutes(15), options.Timeout);
        Assert.True(new IdleLockOptionsValidator().Validate(null, options).Succeeded);
        Assert.Equal("IdleLock", IdleLockOptions.SectionName);
    }
}
