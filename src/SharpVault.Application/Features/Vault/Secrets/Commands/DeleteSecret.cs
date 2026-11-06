// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.Vault.Secrets.Commands;

public sealed record DeleteSecretCommand(Guid SecretId, string ETag) : IRequest<Result<Done>>;

public sealed class DeleteSecretCommandValidator : AbstractValidator<DeleteSecretCommand>
{
    public DeleteSecretCommandValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
        RuleFor(x => x.ETag).ValidIfMatch();
    }
}

public sealed class DeleteSecretCommandHandler(
    IVaultStore store,
    ISecretAccessPolicy accessPolicy,
    VaultAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<DeleteSecretCommandHandler> logger) : IRequestHandler<DeleteSecretCommand, Result<Done>>
{
    public async Task<Result<Done>> Handle(DeleteSecretCommand request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.RequireAsync(request.SecretId, SecretPermission.Owner, cancellationToken);
        if (grant is null)
        {
            return Result<Done>.NotFound();
        }

        var current = await store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        if (current is null)
        {
            return Result<Done>.NotFound();
        }

        if (!string.Equals(current.ETag, request.ETag, StringComparison.Ordinal))
        {
            return Result<Done>.Conflict();
        }

        var secret = current.Value;
        secret.DeletedAt = timeProvider.GetUtcNow();
        secret.DeletionBatchId = Guid.NewGuid();

        var write = await store.SubmitAsync(grant.OwnerId, [VaultChange.RecycleSecret(secret, current.ETag)], cancellationToken);
        if (VaultWrites.ToFailure<Done>(write, logger, secret.Id) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretDeleted, grant.OwnerId, secret.Id, secret.Id));

        return Result<Done>.Success(Done.Value);
    }
}
