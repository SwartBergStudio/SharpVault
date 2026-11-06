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

namespace SharpVault.Application.Features.Vault.History.Commands;

public sealed record RestoreSecretVersionCommand(Guid SecretId, Guid VersionId, string ETag) : IRequest<Result<SecretDto>>;

public sealed class RestoreSecretVersionCommandValidator : AbstractValidator<RestoreSecretVersionCommand>
{
    public RestoreSecretVersionCommandValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
        RuleFor(x => x.VersionId).NotEqual(Guid.Empty);
        RuleFor(x => x.ETag).ValidIfMatch();
    }
}

public sealed class RestoreSecretVersionCommandHandler(
    IVaultStore store,
    VaultKeyRingService keyRings,
    IEnvelopeEncryptor encryptor,
    ISecretAccessPolicy accessPolicy,
    VaultAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<RestoreSecretVersionCommandHandler> logger) : IRequestHandler<RestoreSecretVersionCommand, Result<SecretDto>>
{
    public async Task<Result<SecretDto>> Handle(RestoreSecretVersionCommand request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.RequireAsync(request.SecretId, SecretPermission.Edit, cancellationToken);
        if (grant is null)
        {
            return Result<SecretDto>.NotFound();
        }

        var secretTask = store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        var versionTask = store.GetVersionAsync(grant.OwnerId, request.SecretId, request.VersionId, cancellationToken);
        var vaultTask = keyRings.OpenAsync(grant.OwnerId, cancellationToken);
        await VaultTasks.WhenAllOrDisposeAsync(vaultTask, secretTask, versionTask);

        using var vault = await vaultTask;
        var current = await secretTask;
        var version = await versionTask;
        if (current is null || version is null || vault is null)
        {
            return Result<SecretDto>.NotFound();
        }

        if (!string.Equals(current.ETag, request.ETag, StringComparison.Ordinal))
        {
            return Result<SecretDto>.Conflict();
        }

        var secret = current.Value;
        var now = timeProvider.GetUtcNow();
        using var secretKey = await encryptor.OpenDataKeyAsync(secret.DataKey, cancellationToken);

        var history = new List<VaultChange>();
        if (secret.ValueEnvelope is not null)
        {
            var versions = await store.ListVersionsAsync(grant.OwnerId, secret.Id, cancellationToken);
            history = SecretVersionHistory.ArchiveCurrentValue(secretKey, secret, versions, VaultIds.NewId(timeProvider), now, version.Value.Id);
        }

        secret.ValueEnvelope = SecretValueCipher.PromoteVersion(secretKey, secret, version.Value);
        secret.Revision++;
        secret.ModifiedAt = now;

        var changes = new List<VaultChange> { VaultChange.ReplaceSecret(secret, current.ETag) };
        changes.AddRange(history);

        var write = await store.SubmitAsync(grant.OwnerId, changes, cancellationToken);
        if (VaultWrites.ToFailure<SecretDto>(write, logger, secret.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretVersionRestored, grant.OwnerId, version.Value.Id, secret.Id));

        return Result<SecretDto>.Success(SecretDtoFactory.Create(new Versioned<Secret>(secret, VaultWrites.ETagAt(write, 0)), vault));
    }
}
