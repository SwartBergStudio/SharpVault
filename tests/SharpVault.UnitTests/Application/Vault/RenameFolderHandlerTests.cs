// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Folders.Commands;
using SharpVault.Domain.Audit;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RenameFolderHandlerTests : VaultHandlerTestBase
{
    private RenameFolderCommandHandler Handler() => new(Store, KeyRings, Audit, Current, Time, NullLogger<RenameFolderCommandHandler>.Instance);

    [Fact]
    public async Task Handle_Existing_RenamesAndAudits()
    {
        var parent = Guid.NewGuid();
        var folder = GivenFolder(NewFolder(parent));
        NextWrite = WrittenWith(ETagOf(40));

        var result = await Handler().Handle(new RenameFolderCommand(folder.Value.Id, "  Renamed  "), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", result.Value!.Name);
        Assert.Equal(parent, result.Value.ParentId);
        var change = Assert.Single(LastSubmit);
        Assert.Equal(VaultChangeKind.ReplaceFolder, change.Kind);
        Assert.Equal(Now, change.Folder!.ModifiedAt);
        Assert.Equal("Renamed", MetadataSession.Decrypt(new EncryptionContext(VaultPurposes.FolderName, folder.Value.Id, Owner), change.Folder.NameEnvelope));
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.FolderRenamed, entry.EventType);
        Assert.Equal(folder.Value.Id.ToString("D"), entry.TargetId);
        Assert.Null(entry.SecretId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DamagedName_RejectsWithoutSubmitOrAudit()
    {
        var damaged = NewFolder();
        damaged.NameEnvelope = NewFolder().NameEnvelope;
        var folder = GivenFolder(damaged);

        var result = await Handler().Handle(new RenameFolderCommand(folder.Value.Id, "Renamed"), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.DamagedFolderNotRenamable, result.Error);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_Missing_ReturnsNotFound()
    {
        var result = await Handler().Handle(new RenameFolderCommand(Guid.NewGuid(), "Name"), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_FolderOfAnotherOwner_ReturnsNotFound()
    {
        var foreign = GivenFolder(partition: Guid.NewGuid());

        var result = await Handler().Handle(new RenameFolderCommand(foreign.Value.Id, "Hijacked"), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsNotFound()
    {
        GivenNoKeyRing();
        var folder = GivenFolder();

        var result = await Handler().Handle(new RenameFolderCommand(folder.Value.Id, "Name"), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(Submitted);
    }

    [Fact]
    public async Task Handle_UsesServerReadETag()
    {
        var folder = GivenFolder(etag: ETagOf(33));

        await Handler().Handle(new RenameFolderCommand(folder.Value.Id, "Name"), TestContext.Current.CancellationToken);

        Assert.Equal(ETagOf(33), Assert.Single(LastSubmit).ETag);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    public async Task Handle_SubmitFailure_MapsToResultWithoutAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var folder = GivenFolder();
        NextWrite = Failed(write);

        var result = await Handler().Handle(new RenameFolderCommand(folder.Value.Id, "Name"), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_Conflict_ReturnsConflict()
    {
        var folder = GivenFolder();
        NextWrite = Failed(StoreWriteResult.Conflict);

        var result = await Handler().Handle(new RenameFolderCommand(folder.Value.Id, "Name"), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
    }

    [Fact]
    public async Task Handle_AuditThrows_Propagates()
    {
        var folder = GivenFolder();
        AuditLog.Failure = new TimeoutException("audit down");

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(new RenameFolderCommand(folder.Value.Id, "Name"), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditEntry_ContainsNoFolderName()
    {
        var folder = GivenFolder();
        var marker = "SV-FOLDER-" + Guid.NewGuid().ToString("N");

        await Handler().Handle(new RenameFolderCommand(folder.Value.Id, marker), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(marker, JsonSerializer.Serialize(AuditLog.Entries), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Command_ToString_ReturnsTypeNameOnly()
    {
        Assert.Equal(nameof(RenameFolderCommand), new RenameFolderCommand(Guid.NewGuid(), "secret-name").ToString());
    }
}
