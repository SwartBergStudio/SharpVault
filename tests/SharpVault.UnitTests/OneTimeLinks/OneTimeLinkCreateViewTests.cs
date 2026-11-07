// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.OneTimeLinks;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.OneTimeLinks;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkCreateViewTests : ShellTestContext
{
    private const string Passphrase = "otl-passphrase";
    private const string Output = "otl-link-output";
    private const string Handle = "handle-1";
    private const string BaseAddress = "https://sharpvault.example/";

    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _secret = Guid.NewGuid();
    private readonly Guid _linkId = Guid.NewGuid();
    private readonly IOneTimeLinkCrypto _crypto = Substitute.For<IOneTimeLinkCrypto>();

    public OneTimeLinkCreateViewTests()
    {
        var baseUri = Substitute.For<IOneTimeLinkBaseUri>();
        baseUri.Value.Returns(BaseAddress);
        Services.AddSingleton(_crypto);
        Services.AddSingleton(baseUri);
        ReturnsSettings(Settings());
        OneTimeLinksClient.GetSnapshotAsync(_owner, _secret, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkSnapshotDto>.Success(new OneTimeLinkSnapshotDto { Type = SecretTypeDto.Login, Name = "Bank" })));
        OneTimeLinksClient.CreateAsync(_owner, _secret, Arg.Any<CreateOneTimeLinkRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkDto>.Success(new OneTimeLinkDto { Id = _linkId, ItemName = "Bank" })));
        _crypto.SealAsync(Arg.Any<string>(), Passphrase, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OneTimeLinkSeal(OneTimeLinkSealStatus.Sealed, Handle, [1, 2, 3])));
    }

    [Fact]
    public void Create_Success_CallsSnapshotSealCreateAndPublishInOrder()
    {
        var cut = RenderView();

        cut.WaitForElement("#otl-create").Click();

        cut.WaitForAssertion(() => _crypto.Received(1).PublishAsync(Handle, _linkId, BaseAddress, Output, Arg.Any<CancellationToken>()));
        Received.InOrder(() =>
        {
            OneTimeLinksClient.GetSnapshotAsync(_owner, _secret, Arg.Any<CancellationToken>());
            _crypto.SealAsync(Arg.Any<string>(), Passphrase, Arg.Any<CancellationToken>());
            OneTimeLinksClient.CreateAsync(_owner, _secret, Arg.Any<CreateOneTimeLinkRequestDto>(), Arg.Any<CancellationToken>());
            _crypto.PublishAsync(Handle, _linkId, BaseAddress, Output, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void Create_FeatureDisabled_ShowsDisabledAndNoForm()
    {
        ReturnsSettings(Settings(enabled: false));

        var cut = RenderView();

        cut.WaitForElement("#otl-create-disabled");
        Assert.Empty(cut.FindAll("#otl-create"));
        Assert.Empty(cut.FindAll("#" + Passphrase));
    }

    [Fact]
    public void Create_PassphraseTooShort_StopsBeforeCreate()
    {
        _crypto.SealAsync(Arg.Any<string>(), Passphrase, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OneTimeLinkSeal(OneTimeLinkSealStatus.PassphraseTooShort, null, null)));
        var cut = RenderView();

        cut.WaitForElement("#otl-create").Click();

        cut.WaitForAssertion(() => Assert.Contains(OneTimeLinkCreateView.PassphraseTooShortText, cut.Markup, StringComparison.Ordinal));
        _ = OneTimeLinksClient.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateOneTimeLinkRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_UnsupportedBrowser_StopsBeforeCreate()
    {
        _crypto.SealAsync(Arg.Any<string>(), Passphrase, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OneTimeLinkSeal(OneTimeLinkSealStatus.Unsupported, null, null)));
        var cut = RenderView();

        cut.WaitForElement("#otl-create").Click();

        cut.WaitForAssertion(() => Assert.Contains(OneTimeLinkCreateView.UnsupportedText, cut.Markup, StringComparison.Ordinal));
        _ = OneTimeLinksClient.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateOneTimeLinkRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_ApiFails_DiscardsTheSeal()
    {
        OneTimeLinksClient.CreateAsync(_owner, _secret, Arg.Any<CreateOneTimeLinkRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkDto>.Failed()));
        var cut = RenderView();

        cut.WaitForElement("#otl-create").Click();

        cut.WaitForAssertion(() => _crypto.Received(1).DiscardAsync(Handle, Arg.Any<CancellationToken>()));
        _ = _crypto.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_SnapshotNotFound_ShowsTheStatusAndNeverSeals()
    {
        OneTimeLinksClient.GetSnapshotAsync(_owner, _secret, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkSnapshotDto>.NotFound()));
        var cut = RenderView();

        cut.WaitForElement("#otl-create").Click();

        cut.WaitForAssertion(() => OneTimeLinksClient.Received(1).GetSnapshotAsync(_owner, _secret, Arg.Any<CancellationToken>()));
        _ = _crypto.DidNotReceive().SealAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _ = OneTimeLinksClient.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateOneTimeLinkRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Lifetimes_OverTheMaximum_AreNotOffered()
    {
        ReturnsSettings(Settings(max: 1440, defaultMinutes: 1440));

        var cut = RenderView();

        var select = cut.WaitForComponent<SvSelect>();
        Assert.Contains(select.Instance.Options, option => option.Value == "1440");
        Assert.DoesNotContain(select.Instance.Options, option => option.Value == "4320");
        Assert.DoesNotContain(select.Instance.Options, option => option.Value == "10080");
    }

    [Fact]
    public void Inputs_RemainRenderedAcrossStates()
    {
        _crypto.SealAsync(Arg.Any<string>(), Passphrase, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OneTimeLinkSeal(OneTimeLinkSealStatus.Unsupported, null, null)));
        var cut = RenderView();

        cut.WaitForElement("#otl-create").Click();

        cut.WaitForAssertion(() => Assert.Contains(OneTimeLinkCreateView.UnsupportedText, cut.Markup, StringComparison.Ordinal));
        Assert.NotEmpty(cut.FindAll("#" + Passphrase));
        Assert.NotEmpty(cut.FindAll("#otl-lifetime"));
    }

    private void ReturnsSettings(OneTimeLinkSettingsDto settings) =>
        OneTimeLinksClient.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkSettingsDto>.Success(settings)));

    private static OneTimeLinkSettingsDto Settings(bool enabled = true, int max = 10080, int defaultMinutes = 1440) => new()
    {
        Enabled = enabled,
        MaxLifetimeMinutes = max,
        DefaultLifetimeMinutes = defaultMinutes,
    };

    private IRenderedComponent<OneTimeLinkCreateView> RenderView() =>
        Render<OneTimeLinkCreateView>(parameters => parameters
            .Add(view => view.OwnerId, _owner)
            .Add(view => view.SecretId, _secret));
}
