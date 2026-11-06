// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Users;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class CurrentUserAccessorTests
{
    private static AuthenticationStateProvider Principal(Guid objectId)
    {
        var provider = Substitute.For<AuthenticationStateProvider>();
        provider.GetAuthenticationStateAsync().Returns(State(objectId));
        return provider;
    }

    private static AuthenticationState State(Guid objectId) =>
        new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", objectId.ToString("D"))], "test")));

    private static CurrentUserDto User(string name) => new() { UserId = Guid.NewGuid(), DisplayName = name, IsAdmin = true };

    [Fact]
    public async Task Get_FirstCall_CallsApiOnce()
    {
        var client = Substitute.For<IMeApiClient>();
        client.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(ApiResponse<CurrentUserDto>.Success(User("Alice")));
        var accessor = new CurrentUserAccessor(client, Principal(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var first = await accessor.GetAsync(TestContext.Current.CancellationToken);
        var second = await accessor.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, first.Status);
        Assert.Same(first.Value, second.Value);
        await client.Received(1).GetCurrentUserAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ParallelFirstCalls_ShareOneRequest()
    {
        var gate = new TaskCompletionSource<ApiResponse<CurrentUserDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IMeApiClient>();
        client.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);
        var accessor = new CurrentUserAccessor(client, Principal(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var calls = Enumerable.Range(0, 5).Select(_ => accessor.GetAsync(TestContext.Current.CancellationToken)).ToArray();
        gate.SetResult(ApiResponse<CurrentUserDto>.Success(User("Alice")));
        var results = await Task.WhenAll(calls);

        Assert.All(results, result => Assert.Equal(ApiCallStatus.Success, result.Status));
        await client.Received(1).GetCurrentUserAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_AfterFailure_RetriesOnNextCall()
    {
        var client = Substitute.For<IMeApiClient>();
        client.GetCurrentUserAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResponse<CurrentUserDto>.Failed(), ApiResponse<CurrentUserDto>.Success(User("Alice")));
        var accessor = new CurrentUserAccessor(client, Principal(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var first = await accessor.GetAsync(TestContext.Current.CancellationToken);
        var second = await accessor.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, first.Status);
        Assert.Equal(ApiCallStatus.Success, second.Status);
        await client.Received(2).GetCurrentUserAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_AfterInvalidate_CallsAgain()
    {
        var client = Substitute.For<IMeApiClient>();
        client.GetCurrentUserAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResponse<CurrentUserDto>.Success(User("Alice")), ApiResponse<CurrentUserDto>.Success(User("Bob")));
        var accessor = new CurrentUserAccessor(client, Principal(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var first = await accessor.GetAsync(TestContext.Current.CancellationToken);
        accessor.Invalidate();
        var second = await accessor.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Alice", first.Value!.DisplayName);
        Assert.Equal("Bob", second.Value!.DisplayName);
    }

    [Fact]
    public async Task Get_TwoScopes_DoNotShareCachedUsers()
    {
        var aliceClient = Substitute.For<IMeApiClient>();
        aliceClient.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(ApiResponse<CurrentUserDto>.Success(User("Alice")));
        var bobClient = Substitute.For<IMeApiClient>();
        bobClient.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(ApiResponse<CurrentUserDto>.Success(User("Bob")));

        var alice = await new CurrentUserAccessor(aliceClient, Principal(Guid.Parse("11111111-1111-1111-1111-111111111111"))).GetAsync(TestContext.Current.CancellationToken);
        var bob = await new CurrentUserAccessor(bobClient, Principal(Guid.Parse("11111111-1111-1111-1111-111111111111"))).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Alice", alice.Value!.DisplayName);
        Assert.Equal("Bob", bob.Value!.DisplayName);
    }

    [Fact]
    public async Task Get_CallerCancelsWhileWaiting_ThrowsAndLeavesTheSharedRequestRunning()
    {
        var gate = new TaskCompletionSource<ApiResponse<CurrentUserDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IMeApiClient>();
        client.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);
        var accessor = new CurrentUserAccessor(client, Principal(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        using var source = new CancellationTokenSource();

        var cancelled = accessor.GetAsync(source.Token);
        await source.CancelAsync();
        var survivor = accessor.GetAsync(TestContext.Current.CancellationToken);
        gate.SetResult(ApiResponse<CurrentUserDto>.Success(User("Alice")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(ApiCallStatus.Success, (await survivor).Status);
        await client.Received(1).GetCurrentUserAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_PrincipalChanges_ReloadsProfile()
    {
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authentication = Substitute.For<AuthenticationStateProvider>();
        authentication.GetAuthenticationStateAsync().Returns(State(adminId));
        var client = Substitute.For<IMeApiClient>();
        client.GetCurrentUserAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResponse<CurrentUserDto>.Success(User("Admin One")), ApiResponse<CurrentUserDto>.Success(User("User One")));
        var accessor = new CurrentUserAccessor(client, authentication);

        var first = await accessor.GetAsync(TestContext.Current.CancellationToken);
        authentication.GetAuthenticationStateAsync().Returns(State(userId));
        var second = await accessor.GetAsync(TestContext.Current.CancellationToken);
        var third = await accessor.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Admin One", first.Value!.DisplayName);
        Assert.Equal("User One", second.Value!.DisplayName);
        Assert.Same(second.Value, third.Value);
        await client.Received(2).GetCurrentUserAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_PrincipalChangesWhileLoading_DoesNotCacheTheStaleProfileForTheNewPrincipal()
    {
        var authentication = Substitute.For<AuthenticationStateProvider>();
        authentication.GetAuthenticationStateAsync().Returns(State(Guid.NewGuid()));
        var staleGate = new TaskCompletionSource<ApiResponse<CurrentUserDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IMeApiClient>();
        client.GetCurrentUserAsync(Arg.Any<CancellationToken>())
            .Returns(staleGate.Task, Task.FromResult(ApiResponse<CurrentUserDto>.Success(User("User One"))));
        var accessor = new CurrentUserAccessor(client, authentication);

        var stale = accessor.GetAsync(TestContext.Current.CancellationToken);
        authentication.GetAuthenticationStateAsync().Returns(State(Guid.NewGuid()));
        var fresh = await accessor.GetAsync(TestContext.Current.CancellationToken);
        staleGate.SetResult(ApiResponse<CurrentUserDto>.Success(User("Admin One")));
        await stale;
        var again = await accessor.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal("User One", fresh.Value!.DisplayName);
        Assert.Equal("User One", again.Value!.DisplayName);
    }
}
