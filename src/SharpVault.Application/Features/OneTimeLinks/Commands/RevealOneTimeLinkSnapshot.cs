// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Commands;

public sealed record RevealOneTimeLinkSnapshotCommand(Guid OwnerId, Guid SecretId) : IRequest<Result<OneTimeLinkSnapshotDto>>;

public sealed class RevealOneTimeLinkSnapshotCommandValidator : AbstractValidator<RevealOneTimeLinkSnapshotCommand>
{
    public RevealOneTimeLinkSnapshotCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
    }
}

public sealed class RevealOneTimeLinkSnapshotCommandHandler(
    IOneTimeLinkPolicy policy,
    ShareManagementGate gate,
    IVaultStore store,
    VaultKeyRingService keyRings,
    IEnvelopeEncryptor encryptor,
    OneTimeLinkAuditWriter audit,
    ICurrentUser currentUser,
    IRequestMetadata requestMetadata) : IRequestHandler<RevealOneTimeLinkSnapshotCommand, Result<OneTimeLinkSnapshotDto>>
{
    public async Task<Result<OneTimeLinkSnapshotDto>> Handle(RevealOneTimeLinkSnapshotCommand request, CancellationToken cancellationToken)
    {
        var settings = await policy.GetAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Result<OneTimeLinkSnapshotDto>.Forbidden();
        }

        var permission = await gate.RequireManageAsync(request.OwnerId, ShareItemKind.Secret, request.SecretId, allowRecycled: false, cancellationToken);
        if (permission is null)
        {
            return Result<OneTimeLinkSnapshotDto>.NotFound();
        }

        var current = await store.GetSecretAsync(request.OwnerId, request.SecretId, cancellationToken);
        if (current is null)
        {
            return Result<OneTimeLinkSnapshotDto>.NotFound();
        }

        using var read = await keyRings.OpenForReadAsync(request.OwnerId, cancellationToken);
        if (read.Vault is null || !SecretMetadataCipher.TryDecrypt(read.Vault.Session, current.Value, read.Vault.MetadataKeyVersion, out var metadata))
        {
            return Result<OneTimeLinkSnapshotDto>.Rejected(OneTimeLinkMessages.SecretUnreadable);
        }

        var secret = current.Value;
        using var secretKey = await encryptor.OpenDataKeyAsync(secret.DataKey, cancellationToken);
        var snapshot = new OneTimeLinkSnapshotDto
        {
            Type = VaultEnumMap.ToDto(secret.Type),
            Name = metadata.Name,
            Username = metadata.Username,
            Url = metadata.Url,
            Value = secret.ValueEnvelope is { } valueEnvelope ? SecretValueCipher.DecryptValue(secretKey, secret, valueEnvelope) : null,
            Notes = secret.NotesEnvelope is { } notesEnvelope ? SecretValueCipher.DecryptNotes(secretKey, secret, notesEnvelope) : null,
        };

        await audit.WriteAsync(new OneTimeLinkAuditItem(
            AuditEventType.OneTimeLinkSnapshotRevealed,
            currentUser.UserId,
            request.SecretId,
            request.OwnerId,
            request.SecretId,
            requestMetadata.IpAddress,
            UserAgent: null), cancellationToken);

        return Result<OneTimeLinkSnapshotDto>.Success(snapshot);
    }
}
