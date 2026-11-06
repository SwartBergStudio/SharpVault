// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Api.Security;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Domain.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Api;

public sealed class LocalAccountSeederTests
{
    [Fact]
    public async Task StartAsync_StoreThrowsForEveryAccount_CompletesAndLogsAWarningPerAccount()
    {
        var store = Substitute.For<IUserStore>();
        store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("storage down"));
        var logger = new RecordingLogger<LocalAccountSeeder>();
        var seeder = CreateSeeder(store, logger);

        await seeder.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LocalAccounts.All.Count, logger.Records.Count(record => record.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task StartAsync_OneAccountThrows_StillSeedsTheRest()
    {
        var store = Substitute.For<IUserStore>();
        store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(
            _ => throw new InvalidOperationException("storage down"),
            _ => Task.FromResult(StoreWriteResult.Written));
        var seeder = CreateSeeder(store, new RecordingLogger<LocalAccountSeeder>());

        await seeder.StartAsync(TestContext.Current.CancellationToken);

        await store.Received(LocalAccounts.All.Count).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_AdminAccounts_SetTheFlag()
    {
        var seeded = await SeedAsync();

        Assert.All(
            LocalAccounts.All.Where(account => account.Roles.Contains(AppRoles.Admin)),
            account =>
            {
                var user = seeded.Single(candidate => candidate.Id == account.Id);
                Assert.True(user.IsAdmin);
                Assert.NotNull(user.RoleObservedAt);
            });
        Assert.Equal(2, seeded.Count(user => user.IsAdmin));
    }

    [Fact]
    public async Task Seed_UserAccounts_LeaveItFalse()
    {
        var seeded = await SeedAsync();

        Assert.All(
            LocalAccounts.All.Where(account => !account.Roles.Contains(AppRoles.Admin)),
            account => Assert.False(seeded.Single(candidate => candidate.Id == account.Id).IsAdmin));
    }

    [Fact]
    public async Task StartAsync_Cancelled_Propagates()
    {
        var store = Substitute.For<IUserStore>();
        store.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        var seeder = CreateSeeder(store, new RecordingLogger<LocalAccountSeeder>());

        await Assert.ThrowsAsync<OperationCanceledException>(() => seeder.StartAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<List<User>> SeedAsync()
    {
        var store = Substitute.For<IUserStore>();
        var added = new List<User>();
        store.AddAsync(Arg.Do<User>(added.Add), Arg.Any<CancellationToken>()).Returns(StoreWriteResult.Written);
        var seeder = CreateSeeder(store, new RecordingLogger<LocalAccountSeeder>());

        await seeder.StartAsync(TestContext.Current.CancellationToken);

        return added;
    }

    private static LocalAccountSeeder CreateSeeder(IUserStore store, ILogger<LocalAccountSeeder> logger)
    {
        var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        return new LocalAccountSeeder(services.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(), logger);
    }
}
