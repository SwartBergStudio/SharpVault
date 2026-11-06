// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Sharing.Grants.Commands;
using SharpVault.Application.Features.Sharing.Grants.Queries;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;
using SharpVault.UnitTests.Support.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

[Trait("Category", "Security")]
public sealed class ShareGrantCapRaceTests
{
    private readonly Guid _itemId = Guid.NewGuid();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CreateShareGrantCommandHandler CreateHandler(ShareGrantHandlerWorld world) => new(
        world.Gate,
        world.GrantService,
        world.Chain.Store,
        world.Directory,
        world.Users,
        world.Caller,
        world.Audit,
        world.Time,
        world.LoggerFor<CreateShareGrantCommandHandler>());

    private ShareGrantHandlerWorld WorldAtNinetyNine(int racers)
    {
        var world = new ShareGrantHandlerWorld(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        for (var index = 0; index < SharingLimits.MaxGrantsPerItem - 1; index++)
        {
            world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId, "Existing " + index.ToString("D3", CultureInfo.InvariantCulture));
        }

        var arrived = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.BeforeSubmit = async () =>
        {
            if (Interlocked.Increment(ref arrived) == racers)
            {
                release.SetResult();
            }

            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        };
        return world;
    }

    [Fact]
    public async Task Create_TwoParallelCreatesAtNinetyNine_LeaveEveryStoredGrantVisibleToTheOwner()
    {
        using var world = WorldAtNinetyNine(2);
        var racers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var racer in racers)
        {
            world.KnownMember(racer);
        }

        var results = await Task.WhenAll(racers.Select(racer =>
            CreateHandler(world).Handle(new CreateShareGrantCommand(world.OwnerId, ShareItemKind.Secret, _itemId, racer, SharePermissionDto.View), Token)));

        world.BeforeSubmit = null;
        var listed = await new ListShareGrantsQueryHandler(world.Gate, world.Chain.Store, world.Caller, Microsoft.Extensions.Logging.Abstractions.NullLogger<ListShareGrantsQueryHandler>.Instance)
            .Handle(new ListShareGrantsQuery(world.OwnerId, ShareItemKind.Secret, _itemId), Token);
        var stored = world.Grants.Count;
        Assert.Contains(results, result => result.IsSuccess);
        Assert.True(
            stored <= SharingLimits.MaxGrantsPerItem || listed.Value!.Items.Count == stored,
            $"{stored} grants are stored but the owner is shown {listed.Value!.Items.Count}.");
    }

    [Fact]
    public async Task Create_ParallelCreatesAtNinetyNine_NeverExceedTheCapByMoreThanTheRacers()
    {
        using var world = WorldAtNinetyNine(2);
        var racers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var racer in racers)
        {
            world.KnownMember(racer);
        }

        await Task.WhenAll(racers.Select(racer =>
            CreateHandler(world).Handle(new CreateShareGrantCommand(world.OwnerId, ShareItemKind.Secret, _itemId, racer, SharePermissionDto.View), Token)));

        Assert.True(world.Grants.Count <= SharingLimits.MaxGrantsPerItem + 1);
    }

    [Fact]
    public async Task Create_AtTheCapAfterTheRace_RejectsFurtherGrants()
    {
        using var world = WorldAtNinetyNine(1);
        var first = Guid.NewGuid();
        var next = Guid.NewGuid();
        world.KnownMember(first);
        world.KnownMember(next);
        await CreateHandler(world).Handle(new CreateShareGrantCommand(world.OwnerId, ShareItemKind.Secret, _itemId, first, SharePermissionDto.View), Token);

        var result = await CreateHandler(world).Handle(new CreateShareGrantCommand(world.OwnerId, ShareItemKind.Secret, _itemId, next, SharePermissionDto.View), Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(SharingLimits.MaxGrantsPerItem, world.Grants.Count);
    }
}
