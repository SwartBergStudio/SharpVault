// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Domain.Audit;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkAuditWriterTests
{
    private const string ClientAddress = "203.0.113.77";
    private const string ClientAgent = "SharpVault-Probe/9.9 private-marker";

    [Fact]
    public async Task WriteAsync_RevocationAuditFails_LogHoldsTheLinkIdAndNoPayloadOrAddress()
    {
        var linkId = Guid.NewGuid();
        var logger = new RecordingLogger<OneTimeLinkAuditWriter>();
        var audit = new RecordingAuditLog { Failure = new InvalidOperationException("The audit store is down.") };
        var writer = new OneTimeLinkAuditWriter(audit, TimeProvider.System, logger);
        var item = new OneTimeLinkAuditItem(
            AuditEventType.OneTimeLinkRevoked,
            Guid.NewGuid(),
            linkId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ClientAddress,
            ClientAgent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(item, TestContext.Current.CancellationToken));

        var text = logger.AllText();
        Assert.Contains(linkId.ToString("D"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientAddress, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientAgent, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteCommittedAsync_OpenedAuditFails_LogHoldsTheLinkId()
    {
        var linkId = Guid.NewGuid();
        var logger = new RecordingLogger<OneTimeLinkAuditWriter>();
        var audit = new RecordingAuditLog { Failure = new InvalidOperationException("The audit store is down.") };
        var writer = new OneTimeLinkAuditWriter(audit, TimeProvider.System, logger);
        var item = new OneTimeLinkAuditItem(
            AuditEventType.OneTimeLinkOpened,
            AuditActors.AnonymousLinkRecipient,
            linkId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ClientAddress,
            ClientAgent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteCommittedAsync(item));

        Assert.Contains(linkId.ToString("D"), logger.AllText(), StringComparison.Ordinal);
    }
}
