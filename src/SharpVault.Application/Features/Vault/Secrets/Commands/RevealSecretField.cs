// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Secrets.Commands;

public sealed record RevealSecretFieldCommand(Guid SecretId, RevealFieldDto Field, RevealIntentDto Intent, Guid? SharedOwnerId = null)
    : IRequest<Result<RevealedValueDto>>;

public sealed class RevealSecretFieldCommandValidator : AbstractValidator<RevealSecretFieldCommand>
{
    public RevealSecretFieldCommandValidator()
    {
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);
        RuleFor(x => x.Field).IsInEnum();
        RuleFor(x => x.Intent).IsInEnum();
        RuleFor(x => x.SharedOwnerId).NotEqual(Guid.Empty).When(x => x.SharedOwnerId is not null);
    }
}

public sealed class RevealSecretFieldCommandHandler(
    IVaultStore store,
    IEnvelopeEncryptor encryptor,
    ISecretAccessPolicy accessPolicy,
    VaultAuditWriter audit) : IRequestHandler<RevealSecretFieldCommand, Result<RevealedValueDto>>
{
    public async Task<Result<RevealedValueDto>> Handle(RevealSecretFieldCommand request, CancellationToken cancellationToken)
    {
        var grant = await accessPolicy.RequireAsync(request.SharedOwnerId, request.SecretId, SecretPermission.View, cancellationToken);
        if (grant is null)
        {
            return Result<RevealedValueDto>.NotFound();
        }

        var current = await store.GetSecretAsync(grant.OwnerId, request.SecretId, cancellationToken);
        if (current is null || EnvelopeOf(current.Value, request.Field) is not { } envelope)
        {
            return Result<RevealedValueDto>.NotFound();
        }

        var secret = current.Value;
        using var secretKey = await encryptor.OpenDataKeyAsync(secret.DataKey, cancellationToken);
        var text = request.Field == RevealFieldDto.Value
            ? SecretValueCipher.DecryptValue(secretKey, secret, envelope)
            : SecretValueCipher.DecryptNotes(secretKey, secret, envelope);

        await audit.WriteAsync(new VaultAuditItem(EventOf(request.Field, request.Intent), grant.OwnerId, secret.Id, secret.Id), cancellationToken);

        return Result<RevealedValueDto>.Success(new RevealedValueDto { Value = text });
    }

    private static byte[]? EnvelopeOf(Secret secret, RevealFieldDto field) => field switch
    {
        RevealFieldDto.Value => secret.ValueEnvelope,
        RevealFieldDto.Notes => secret.NotesEnvelope,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    private static AuditEventType EventOf(RevealFieldDto field, RevealIntentDto intent) => (field, intent) switch
    {
        (RevealFieldDto.Value, RevealIntentDto.View) => AuditEventType.SecretValueRevealed,
        (RevealFieldDto.Value, RevealIntentDto.Copy) => AuditEventType.SecretValueCopied,
        (RevealFieldDto.Notes, RevealIntentDto.View) => AuditEventType.SecretNotesRevealed,
        (RevealFieldDto.Notes, RevealIntentDto.Copy) => AuditEventType.SecretNotesCopied,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };
}
