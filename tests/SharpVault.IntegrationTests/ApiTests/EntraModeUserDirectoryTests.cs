// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Directory;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class EntraModeUserDirectoryTests(ApiTestFactory factory)
{
    [Fact]
    public void ApiTestFactory_Host_ResolvesTheGraphDirectory()
    {
        using var scope = factory.Services.CreateScope();

        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        Assert.IsType<GraphUserDirectory>(directory);
    }
}
