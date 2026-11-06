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

public sealed record CreateSecretCommand(
    SecretTypeDto Type,
    string Name,
    string? Username,
    string? Url,
    string? Value,
    string? Notes,
    IReadOnlyList<string>? Tags,
    Guid? FolderId) : IRequest<Result<SecretDto>>
{
    public override string ToString() => nameof(CreateSecretCommand);
}

public sealed class CreateSecretCommandValidator : AbstractValidator<CreateSecretCommand>
{
    public CreateSecretCommandValidator()
    {
        RuleFor(x => x.Type).IsInEnum();

        this.AddMetadataRules(x => x.Name, x => x.Username, x => x.Url, x => x.Tags);
        this.AddSecretTextRules(x => x.Value, x => x.Notes);

        RuleFor(x => x.FolderId)
            .NotEqual(Guid.Empty)
            .When(x => x.FolderId is not null);
    }
}

public sealed class CreateSecretCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    IEnvelopeEncryptor encryptor,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<CreateSecretCommandHandler> logger) : IRequestHandler<CreateSecretCommand, Result<SecretDto>>
{
    public async Task<Result<SecretDto>> Handle(CreateSecretCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        Versioned<Folder>? folder = null;
        if (request.FolderId is { } folderId)
        {
            folder = await store.GetFolderAsync(owner, folderId, cancellationToken);
            if (folder is null)
            {
                return Result<SecretDto>.NotFound();
            }
        }

        using var vault = await keyRings.OpenOrCreateAsync(owner, cancellationToken);
        using var secretKey = await encryptor.CreateDataKeyAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var secret = new Secret
        {
            Id = VaultIds.NewId(timeProvider),
            OwnerId = owner,
            Type = VaultEnumMap.ToDomain(request.Type),
            FolderId = request.FolderId,
            DataKey = secretKey.WrappedKey,
            MetadataKeyVersion = vault.MetadataKeyVersion,
            Revision = 1,
            CreatedAt = now,
            ModifiedAt = now,
        };

        SecretMetadataCipher.Encrypt(vault.Session, secret, new SecretMetadata(request.Name, request.Username, request.Url, request.Tags ?? []));
        secret.ValueEnvelope = SecretValueCipher.EncryptValue(secretKey, secret, request.Value);
        secret.NotesEnvelope = SecretValueCipher.EncryptNotes(secretKey, secret, request.Notes);

        var changes = new List<VaultChange>();
        if (folder is not null)
        {
            changes.Add(VaultChange.TouchFolder(folder.Value.Id, folder.ETag));
        }

        changes.Add(VaultChange.AddSecret(secret));

        var write = await store.SubmitAsync(owner, changes, cancellationToken);
        if (VaultWrites.ToFailure<SecretDto>(write, logger, secret.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretCreated, owner, secret.Id, secret.Id));

        var etag = VaultWrites.ETagAt(write, changes.Count - 1);
        return Result<SecretDto>.Success(SecretDtoFactory.Create(new Versioned<Secret>(secret, etag), vault));
    }
}
