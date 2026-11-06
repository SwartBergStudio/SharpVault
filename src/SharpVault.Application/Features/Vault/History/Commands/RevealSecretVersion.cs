// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.History.Commands;

public sealed record RevealSecretVersionCommand(Guid SecretId, Guid VersionId, RevealIntentDto Intent)
    : IRequest<Result<RevealedValueDto>>;

public sealed class RevealSecretVersionCommandValidator : AbstractValidator<RevealSecretVersionCommand>
{
    public RevealSecretVersionCommandValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
        RuleFor(x => x.VersionId).NotEqual(Guid.Empty);
        RuleFor(x => x.Intent).IsInEnum();
    }
}

public sealed class RevealSecretVersionCommandHandler(
    IVaultStore store,
    IEnvelopeEncryptor encryptor,
    ISecretAccessPolicy accessPolicy,
    VaultAuditWriter audit) : IRequestHandler<RevealSecretVersionCommand, Result<RevealedValueDto>>
{
    public async Task<Result<RevealedValueDto>> Handle(RevealSecretVersionCommand request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.RequireAsync(request.SecretId, SecretPermission.View, cancellationToken);
        if (grant is null)
        {
            return Result<RevealedValueDto>.NotFound();
        }

        var secretTask = store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        var versionTask = store.GetVersionAsync(grant.OwnerId, request.SecretId, request.VersionId, cancellationToken);
        await Task.WhenAll(secretTask, versionTask);

        var secret = await secretTask;
        var version = await versionTask;
        if (secret is null || version is null)
        {
            return Result<RevealedValueDto>.NotFound();
        }

        using var secretKey = await encryptor.OpenDataKeyAsync(secret.Value.DataKey, cancellationToken);
        var text = SecretValueCipher.DecryptVersion(secretKey, secret.Value, version.Value);

        var eventType = request.Intent == RevealIntentDto.View ? AuditEventType.SecretVersionRevealed : AuditEventType.SecretVersionCopied;
        await audit.WriteAsync(new VaultAuditItem(eventType, grant.OwnerId, version.Value.Id, secret.Value.Id), cancellationToken);

        return Result<RevealedValueDto>.Success(new RevealedValueDto { Value = text });
    }
}
