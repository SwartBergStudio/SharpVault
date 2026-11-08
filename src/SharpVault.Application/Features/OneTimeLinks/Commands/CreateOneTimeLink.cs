// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Commands;

public sealed record CreateOneTimeLinkCommand(Guid OwnerId, Guid SecretId, string Payload, int? LifetimeMinutes) : IRequest<Result<OneTimeLinkDto>>
{
    public override string ToString() => nameof(CreateOneTimeLinkCommand);
}

public sealed class CreateOneTimeLinkCommandValidator : AbstractValidator<CreateOneTimeLinkCommand>
{
    private const int Base64QuadBytes = 3;
    private const int Base64QuadChars = 4;
    private const int MinPayloadBytes = 30;
    private const byte MinVersionByte = 0x80;
    private const int MinLifetimeMinutes = OneTimeLinkLimits.MinLifetimeMinutes;
    private static readonly int MaxLifetimeMinutes = (int)OneTimeLinkLimits.AbsoluteMaxLifetime.TotalMinutes;
    private static readonly int MaxBase64Length = (OneTimeLinkLimits.MaxPayloadBytes + Base64QuadBytes - 1) / Base64QuadBytes * Base64QuadChars;

    public CreateOneTimeLinkCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.SecretId).NotEqual(Guid.Empty);

        RuleFor(x => x.Payload)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(IsWellFormedPayload)
            .WithMessage(OneTimeLinkMessages.PayloadInvalid);

        RuleFor(x => x.LifetimeMinutes)
            .InclusiveBetween(MinLifetimeMinutes, MaxLifetimeMinutes)
            .WithMessage(OneTimeLinkMessages.LifetimeOutOfRange)
            .When(x => x.LifetimeMinutes is not null);
    }

    private static bool IsWellFormedPayload(string payload)
    {
        if (payload.Length > MaxBase64Length)
        {
            return false;
        }

        var buffer = new byte[payload.Length];
        if (!Convert.TryFromBase64String(payload, buffer, out var written))
        {
            return false;
        }

        return written >= MinPayloadBytes && written <= OneTimeLinkLimits.MaxPayloadBytes && buffer[0] >= MinVersionByte;
    }
}

public sealed class CreateOneTimeLinkCommandHandler(
    IOneTimeLinkPolicy policy,
    ShareManagementGate gate,
    IVaultStore store,
    IOneTimeLinkStore links,
    OneTimeLinkAuditWriter audit,
    ICurrentUser currentUser,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider) : IRequestHandler<CreateOneTimeLinkCommand, Result<OneTimeLinkDto>>
{
    private const int RandomLinkIdBytes = 16;

    public async Task<Result<OneTimeLinkDto>> Handle(CreateOneTimeLinkCommand request, CancellationToken cancellationToken)
    {
        var settings = await policy.GetAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Result<OneTimeLinkDto>.Forbidden();
        }

        var permission = await gate.RequireManageAsync(request.OwnerId, ShareItemKind.Secret, request.SecretId, allowRecycled: false, cancellationToken);
        if (permission is null)
        {
            return Result<OneTimeLinkDto>.NotFound();
        }

        var secret = await store.GetSecretAsync(request.OwnerId, request.SecretId, cancellationToken);
        if (secret is null)
        {
            return Result<OneTimeLinkDto>.NotFound();
        }

        var requestedLifetime = request.LifetimeMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : (TimeSpan?)null;
        if (requestedLifetime is { } explicitLifetime && explicitLifetime > settings.MaxLifetime)
        {
            return Result<OneTimeLinkDto>.Rejected(OneTimeLinkMessages.LifetimeTooLong);
        }

        var lifetime = requestedLifetime ?? (OneTimeLinkLimits.DefaultLifetime < settings.MaxLifetime ? OneTimeLinkLimits.DefaultLifetime : settings.MaxLifetime);

        var now = timeProvider.GetUtcNow();
        var active = await links.ListIndexAsync(currentUser.UserId, now, OneTimeLinkLimits.MaxActiveLinksPerCreator, afterLinkId: null, cancellationToken);
        if (active.Items.Count >= OneTimeLinkLimits.MaxActiveLinksPerCreator)
        {
            return Result<OneTimeLinkDto>.Rejected(OneTimeLinkMessages.TooManyActiveLinks);
        }

        var link = new OneTimeLink
        {
            Id = new Guid(RandomNumberGenerator.GetBytes(RandomLinkIdBytes)),
            CreatorId = currentUser.UserId,
            OwnerId = request.OwnerId,
            SecretId = request.SecretId,
            Payload = Convert.FromBase64String(request.Payload),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
        var index = new OneTimeLinkIndexEntry
        {
            LinkId = link.Id,
            CreatorId = link.CreatorId,
            OwnerId = link.OwnerId,
            SecretId = link.SecretId,
            NameEnvelope = secret.Value.NameEnvelope,
            MetadataKeyVersion = secret.Value.MetadataKeyVersion,
            CreatedAt = now,
            ExpiresAt = link.ExpiresAt,
        };

        var written = await links.CreateAsync(link, index, cancellationToken);
        if (written == StoreWriteResult.AlreadyExists)
        {
            return Result<OneTimeLinkDto>.Conflict();
        }

        await audit.WriteCommittedAsync(new OneTimeLinkAuditItem(
            AuditEventType.OneTimeLinkCreated,
            currentUser.UserId,
            link.Id,
            link.OwnerId,
            link.SecretId,
            requestMetadata.IpAddress,
            UserAgent: null));

        return Result<OneTimeLinkDto>.Success(new OneTimeLinkDto
        {
            Id = link.Id,
            OwnerId = link.OwnerId,
            ItemId = link.SecretId,
            ItemName = null,
            CreatedAt = link.CreatedAt,
            ExpiresAt = link.ExpiresAt,
        });
    }
}
