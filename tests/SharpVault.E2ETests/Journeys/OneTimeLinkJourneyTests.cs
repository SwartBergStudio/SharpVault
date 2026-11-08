// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(UserJourneyGroup))]
[Trait("Category", "E2E")]
public sealed class OneTimeLinkJourneyTests(E2EHost host)
{
    private const string UsernameValue = "user.one@sharpvault.invalid";
    private const string ValueMarkerPrefix = "otl-value-marker-";
    private const string PassphrasePrefix = "otl-passphrase-";
    private const string TypedPassphrasePrefix = "typed-marker-";
    private const string WrongPassphrase = "wrong-passphrase-value";
    private const string KeystrokeFrameMarker = "\"key\":\"";
    private const string FragmentPassphrasePrefix = "p.";
    private const string OpenSuffix = "/open";
    private const string FailedResourceConsoleText = "Failed to load resource";
    private const int MaxSecretValueLength = 8192;
    private const int LinkKeyLength = 43;
    private const char MarkupFiller = '<';

    [Fact]
    public async Task OneTimeLinkCreate_HubTraffic_NeverCarriesTheFragmentKeyOrTheLinkUrl()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var passphrase = PassphrasePrefix + suffix;
        var valueMarker = ValueMarkerPrefix + suffix;
        var maximumValue = valueMarker + new string(MarkupFiller, MaxSecretValueLength - valueMarker.Length);

        await using var owner = await host.OpenSessionAsync();
        var ownerPage = await owner.NewPageAsync();
        var frames = new WebSocketFrames();
        ownerPage.WebSocket += (_, socket) => frames.Attach(socket);

        await SignInAsync(ownerPage, E2EAccounts.UserOne);
        var secretId = await CreateSecretAsync(ownerPage, "0 OTL Maximum " + suffix, maximumValue);

        var create = new OneTimeLinkCreatePage(ownerPage, host.WebUrl);
        await create.OpenFromSecretPageAsync();
        await create.SetPassphraseAsync(passphrase);
        var linkUrl = await create.CreateLinkAsync();
        var key = KeyOf(linkUrl);
        Assert.Equal(LinkKeyLength, key.Length);

        frames.AssertNoFrameContains(key, "the Base64Url link key");
        frames.AssertNoFrameContains(linkUrl, "the one-time link URL");
        frames.AssertNoFrameContains(passphrase, "the passphrase");
        owner.Errors.AssertClean(ownerPage.Url);
    }

    [Fact]
    public async Task OneTimeLinkCreate_TypingAPassphrase_SendsNoKeystrokeOverTheCircuit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var marker = TypedPassphrasePrefix + suffix;

        await using var owner = await host.OpenSessionAsync();
        var ownerPage = await owner.NewPageAsync();
        await SignInAsync(ownerPage, E2EAccounts.UserOne);
        var secretId = await CreateSecretAsync(ownerPage, "0 OTL Typed " + suffix, "typed-secret-" + suffix);

        var frames = new WebSocketFrames();
        ownerPage.WebSocket += (_, socket) => frames.Attach(socket);
        var create = new OneTimeLinkCreatePage(ownerPage, host.WebUrl);
        await create.OpenAsync(E2EAccounts.UserOneId, secretId);
        Assert.True(frames.SocketCount > 0, "The create page opened no WebSocket, so the frame capture proves nothing.");
        frames.Clear();

        await create.TypePassphraseAsync(marker);

        Assert.Equal(marker, await create.PassphraseValueAsync());
        frames.AssertNoFrameContains(KeystrokeFrameMarker, "a keystroke event");
        frames.AssertNoFrameContains(marker, "the typed passphrase");
        owner.Errors.AssertClean(ownerPage.Url);
    }

    [Fact]
    public async Task OneTimeLinkOpen_Journey_OpensOnceInAFreshAnonymousContextAndTheNoticeReachesTheCreator()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var passphrase = PassphrasePrefix + suffix;
        var valueMarker = ValueMarkerPrefix + suffix;

        await using var owner = await host.OpenSessionAsync();
        var ownerPage = await owner.NewPageAsync();
        await SignInAsync(ownerPage, E2EAccounts.UserOne);
        var secretId = await CreateSecretAsync(ownerPage, "0 OTL Open " + suffix, valueMarker);
        var create = new OneTimeLinkCreatePage(ownerPage, host.WebUrl);
        await create.OpenFromSecretPageAsync();
        await create.SetPassphraseAsync(passphrase);
        var linkUrl = await create.CreateLinkAsync();
        var linkId = LinkIdOf(linkUrl);
        var key = KeyOf(linkUrl);

        await using var anonymous = await host.OpenSessionAsync();
        var anonymousPage = await anonymous.NewPageAsync();
        var requests = new List<string>();
        anonymousPage.Request += (_, request) => requests.Add(request.Url + " " + (request.PostData ?? string.Empty));
        var open = new OneTimeLinkOpenPage(anonymousPage);
        await open.OpenAsync(linkUrl);
        await open.ClickOpenAsync();
        await open.AssertPassphraseFormShownAsync();
        var requestsBeforeWrongAttempt = requests.Count;

        await open.SubmitPassphraseAsync(WrongPassphrase);
        await open.AssertWrongPassphraseAsync();
        Assert.Equal(requestsBeforeWrongAttempt, requests.Count);

        await open.SubmitPassphraseAsync(passphrase);
        await open.AssertValueAsync(valueMarker);
        await open.AssertAddressBarHasNoFragmentAsync();
        await open.ReloadAsync();
        await open.AssertUnavailableAsync();

        anonymous.Errors.IgnoreConsoleErrorsContaining(FailedResourceConsoleText);
        var revisitPage = await anonymous.NewPageAsync();
        revisitPage.Request += (_, request) => requests.Add(request.Url + " " + (request.PostData ?? string.Empty));
        var revisit = new OneTimeLinkOpenPage(revisitPage);
        await revisit.OpenAsync(linkUrl);
        await revisit.ClickOpenAsync();
        await revisit.AssertUnavailableAsync();

        Assert.Contains(requests, request => request.Contains(linkId.ToString("D") + OpenSuffix, StringComparison.Ordinal));
        Assert.DoesNotContain(requests, request => request.Contains(key, StringComparison.Ordinal));
        Assert.DoesNotContain(requests, request => request.Contains(linkUrl, StringComparison.Ordinal));
        anonymous.Errors.AssertClean(anonymousPage.Url);

        var notices = new OneTimeLinksPage(ownerPage, host.WebUrl);
        await notices.OpenAsync();
        await notices.AssertNoticeForLinkAsync(linkId);

        await create.OpenAsync(E2EAccounts.UserOneId, secretId);
        var signedInLink = await create.CreateLinkAsync();
        var signedInPage = await owner.NewPageAsync();
        var signedInFrames = new WebSocketFrames();
        signedInPage.WebSocket += (_, socket) => signedInFrames.Attach(socket);
        var signedInOpen = new OneTimeLinkOpenPage(signedInPage);
        await signedInOpen.OpenAsync(signedInLink);
        await signedInOpen.ClickOpenAsync();
        await signedInOpen.AssertValueAsync(valueMarker);
        Assert.Equal(0, signedInFrames.SocketCount);
        owner.Errors.AssertClean(ownerPage.Url);
    }

    private async Task SignInAsync(IPage page, string displayName)
    {
        var signIn = new LocalSignInPage(page, host.WebUrl);
        await signIn.OpenAsync();
        await signIn.SignInAsAsync(displayName);
    }

    private async Task<Guid> CreateSecretAsync(IPage page, string name, string value)
    {
        var vault = new VaultPage(page, host.WebUrl);
        await vault.OpenAsync();
        await vault.CreateLoginAsync(name, UsernameValue, value);
        return new SecretDetailPage(page).SecretId();
    }

    private static Guid LinkIdOf(string linkUrl)
    {
        var segments = new Uri(linkUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Guid.Parse(segments[^1]);
    }

    private static string KeyOf(string linkUrl)
    {
        var fragment = linkUrl[(linkUrl.IndexOf('#') + 1)..];
        return fragment.StartsWith(FragmentPassphrasePrefix, StringComparison.Ordinal) ? fragment[FragmentPassphrasePrefix.Length..] : fragment;
    }

    [Collection(nameof(SharingLimitGroup))]
    [Trait("Category", "E2E")]
    public sealed class OneTimeLinkRetryJourneyTests(SharingLimitHost host)
    {
        private const string UsernameValue = "user.one@sharpvault.invalid";
        private const string RetryPassphrasePrefix = "otl-retry-passphrase-";
        private const string RateLimitedText = "Too many requests. Try again in a minute.";
        private const string PassphraseFragmentPrefix = "p.";
        private static readonly TimeSpan RateWindowReset = TimeSpan.FromSeconds(61);

        [Fact]
        public async Task OneTimeLinkCreate_RetryAfterARateLimitedAttempt_StillSealsWithThePassphrase()
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var passphrase = RetryPassphrasePrefix + suffix;

            await using var owner = await host.OpenSessionAsync();
            var ownerPage = await owner.NewPageAsync();
            await SignInAsync(ownerPage, E2EAccounts.UserOne);
            var firstSecretId = await CreateSecretAsync(ownerPage, "0 OTL Retry First " + suffix, "retry-first-" + suffix);
            var secondSecretId = await CreateSecretAsync(ownerPage, "0 OTL Retry Second " + suffix, "retry-second-" + suffix);

            var create = new OneTimeLinkCreatePage(ownerPage, host.WebUrl);
            await create.OpenAsync(E2EAccounts.UserOneId, firstSecretId);
            await create.CreateLinkAsync();

            await create.OpenAsync(E2EAccounts.UserOneId, secondSecretId);
            await create.SetPassphraseAsync(passphrase);
            await create.ClickCreateAsync();
            await create.ShowsStatusAsync(RateLimitedText);
            Assert.Equal(passphrase, await create.PassphraseValueAsync());

            await Task.Delay(RateWindowReset, TestContext.Current.CancellationToken);
            var linkUrl = await create.CreateLinkAsync();

            Assert.StartsWith(PassphraseFragmentPrefix, FragmentOf(linkUrl), StringComparison.Ordinal);
            owner.Errors.AssertClean(ownerPage.Url);
        }

        private async Task SignInAsync(IPage page, string displayName)
        {
            var signIn = new LocalSignInPage(page, host.WebUrl);
            await signIn.OpenAsync();
            await signIn.SignInAsAsync(displayName);
        }

        private async Task<Guid> CreateSecretAsync(IPage page, string name, string value)
        {
            var vault = new VaultPage(page, host.WebUrl);
            await vault.OpenAsync();
            await vault.CreateLoginAsync(name, UsernameValue, value);
            return new SecretDetailPage(page).SecretId();
        }

        private static string FragmentOf(string linkUrl) => linkUrl[(linkUrl.IndexOf('#') + 1)..];
    }
}
