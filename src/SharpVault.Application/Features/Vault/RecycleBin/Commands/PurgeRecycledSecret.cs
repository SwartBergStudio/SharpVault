// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.Vault.RecycleBin.Commands;

public sealed record PurgeRecycledSecretCommand(Guid SecretId) : IRequest<Result<Done>>;

public sealed class PurgeRecycledSecretCommandValidator : AbstractValidator<PurgeRecycledSecretCommand>
{
    public PurgeRecycledSecretCommandValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
    }
}

public sealed class PurgeRecycledSecretCommandHandler(
    IVaultStore store,
    RecycledItemEraser eraser,
    VaultAuditWriter audit,
    ICurrentUser currentUser,
    ILogger<PurgeRecycledSecretCommandHandler> logger) : IRequestHandler<PurgeRecycledSecretCommand, Result<Done>>
{
    public async Task<Result<Done>> Handle(PurgeRecycledSecretCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;

        var recycled = await store.GetRecycledSecretAsync(owner, request.SecretId, cancellationToken);
        if (recycled is null)
        {
            return Result<Done>.NotFound();
        }

        var erased = await eraser.EraseSecretAsync(owner, recycled, cancellationToken);
        await audit.WriteCommittedManyAsync(erased.CommittedGrantAuditItems());
        if (VaultWrites.ToFailure<Done>(erased.Write, logger, request.SecretId) is { } failure)
        {
            return failure;
        }

        await audit.WriteCommittedAsync(new VaultAuditItem(AuditEventType.SecretPurged, owner, request.SecretId, request.SecretId));

        return Result<Done>.Success(Done.Value);
    }
}
