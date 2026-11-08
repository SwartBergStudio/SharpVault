// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.OneTimeLinks;
using SharpVault.UI.Shared.Pages.OneTimeLinks;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.OneTimeLinks;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkCreatePageTests : ShellTestContext
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _secret = Guid.NewGuid();

    public OneTimeLinkCreatePageTests()
    {
        Services.AddSingleton(Substitute.For<IOneTimeLinkCrypto>());
        var baseUri = Substitute.For<IOneTimeLinkBaseUri>();
        baseUri.Value.Returns("https://sharpvault.example/");
        Services.AddSingleton(baseUri);
    }

    [Fact]
    public void Route_PassesTheOwnerAndSecretIdsToTheView()
    {
        var cut = Render<OneTimeLinkCreatePage>(parameters => parameters
            .Add(page => page.OwnerId, _owner)
            .Add(page => page.SecretId, _secret));

        var view = cut.WaitForComponent<OneTimeLinkCreateView>();
        Assert.Equal(_owner, view.Instance.OwnerId);
        Assert.Equal(_secret, view.Instance.SecretId);
    }
}
