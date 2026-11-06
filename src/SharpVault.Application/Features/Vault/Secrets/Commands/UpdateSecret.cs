// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Secrets.Commands;

public sealed record UpdateSecretCommand(
    Guid SecretId,
    string ETag,
    SecretTypeDto Type,
    string Name,
    string? Username,
    string? Url,
    IReadOnlyList<string>? Tags,
    bool ReplaceValue,
    string? Value,
    bool ReplaceNotes,
    string? Notes,
    Guid? SharedOwnerId = null) : IRequest<Result<SecretDto>>
{
    public override string ToString() => nameof(UpdateSecretCommand);
}

public sealed class UpdateSecretCommandValidator : AbstractValidator<UpdateSecretCommand>
{
    public UpdateSecretCommandValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
        RuleFor(x => x.SharedOwnerId).NotEqual(Guid.Empty).When(x => x.SharedOwnerId is not null);
        RuleFor(x => x.ETag).ValidIfMatch();
        RuleFor(x => x.Type).IsInEnum();

        this.AddMetadataRules(x => x.Name, x => x.Username, x => x.Url, x => x.Tags);
        this.AddSecretTextRules(x => x.Value, x => x.Notes);

        RuleFor(x => x.Value)
            .Null()
            .When(x => !x.ReplaceValue)
            .WithMessage(VaultMessages.ValueWithoutReplace);

        RuleFor(x => x.Notes)
            .Null()
            .When(x => !x.ReplaceNotes)
            .WithMessage(VaultMessages.NotesWithoutReplace);
    }
}

public sealed class UpdateSecretCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    IEnvelopeEncryptor encryptor,
    ISecretAccessPolicy accessPolicy,
    VaultAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<UpdateSecretCommandHandler> logger) : IRequestHandler<UpdateSecretCommand, Result<SecretDto>>
{
    public async Task<Result<SecretDto>> Handle(UpdateSecretCommand request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.RequireAsync(request.SharedOwnerId, request.SecretId, SecretPermission.Edit, cancellationToken);
        if (grant is null)
        {
            return Result<SecretDto>.NotFound();
        }

        var secretTask = store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        var vaultTask = keyRings.OpenAsync(grant.OwnerId, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(vaultTask, secretTask);

        using var vault = await vaultTask;
        var current = await secretTask;
        if (current is null || vault is null)
        {
            return Result<SecretDto>.NotFound();
        }

        if (!string.Equals(current.ETag, request.ETag, StringComparison.Ordinal))
        {
            return Result<SecretDto>.Conflict();
        }

        if (SecretDtoFactory.Create(current, vault).IsDamaged)
        {
            return Result<SecretDto>.Rejected(VaultMessages.DamagedSecretNotEditable);
        }

        var secret = current.Value;
        var now = timeProvider.GetUtcNow();
        secret.Type = VaultEnumMap.ToDomain(request.Type);
        secret.MetadataKeyVersion = vault.MetadataKeyVersion;
        SecretMetadataCipher.Encrypt(vault.Session, secret, new SecretMetadata(request.Name, request.Username, request.Url, request.Tags ?? []));

        var history = await ApplyReplacementsAsync(grant.OwnerId, secret, request, now, cancellationToken);
        secret.Revision++;
        secret.ModifiedAt = now;

        var changes = new List<VaultChange> { VaultChange.ReplaceSecret(secret, current.ETag) };
        changes.AddRange(history);

        var write = await store.SubmitAsync(grant.OwnerId, changes, cancellationToken);
        if (VaultWrites.ToFailure<SecretDto>(write, logger, secret.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretUpdated, grant.OwnerId, secret.Id, secret.Id));

        var updated = new Versioned<Secret>(secret, VaultWrites.ETagAt(write, 0));
        return Result<SecretDto>.Success(grant.Granted == SecretPermission.Owner
            ? SecretDtoFactory.Create(updated, vault)
            : SecretDtoFactory.ForRecipient(updated, vault));
    }

    private async Task<List<VaultChange>> ApplyReplacementsAsync(
        Guid owner,
        Secret secret,
        UpdateSecretCommand request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var history = new List<VaultChange>();
        if (!request.ReplaceValue && !request.ReplaceNotes)
        {
            return history;
        }

        using var secretKey = await encryptor.OpenDataKeyAsync(secret.DataKey, cancellationToken);
        if (request.ReplaceValue)
        {
            if (secret.ValueEnvelope is not null)
            {
                var versions = await store.ListVersionsAsync(owner, secret.Id, cancellationToken);
                history.AddRange(SecretVersionHistory.ArchiveCurrentValue(secretKey, secret, versions, VaultIds.NewId(timeProvider), now));
            }

            secret.ValueEnvelope = SecretValueCipher.EncryptValue(secretKey, secret, request.Value);
        }

        if (request.ReplaceNotes)
        {
            secret.NotesEnvelope = SecretValueCipher.EncryptNotes(secretKey, secret, request.Notes);
        }

        return history;
    }
}
