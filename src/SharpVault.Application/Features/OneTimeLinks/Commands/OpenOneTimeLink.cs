// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Domain.Users;
using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Commands;

public sealed record OpenOneTimeLinkCommand(Guid LinkId, string? ClientIp, string? UserAgent) : IRequest<Result<OneTimeLinkPayloadDto>>
{
    public override string ToString() => nameof(OpenOneTimeLinkCommand);
}

public sealed class OpenOneTimeLinkCommandValidator : AbstractValidator<OpenOneTimeLinkCommand>
{
    public OpenOneTimeLinkCommandValidator()
    {
        RuleFor(x => x.LinkId).NotEqual(Guid.Empty);
    }
}

public sealed partial class OpenOneTimeLinkCommandHandler(
    IOneTimeLinkPolicy policy,
    IOneTimeLinkStore links,
    IUserStore users,
    IVaultStore vault,
    OneTimeLinkAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<OpenOneTimeLinkCommandHandler> logger) : IRequestHandler<OpenOneTimeLinkCommand, Result<OneTimeLinkPayloadDto>>
{
    private const string ReadIndexStep = "read index";
    private const string AddNoticeStep = "add notice";
    private const string RemoveBookkeepingStep = "remove bookkeeping";

    public async Task<Result<OneTimeLinkPayloadDto>> Handle(OpenOneTimeLinkCommand request, CancellationToken cancellationToken)
    {
        var settings = await policy.GetAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Result<OneTimeLinkPayloadDto>.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        var stored = await links.GetAsync(request.LinkId, cancellationToken);
        if (stored is null || stored.Value.ExpiresAt <= now)
        {
            return Result<OneTimeLinkPayloadDto>.NotFound();
        }

        var creator = await users.GetAsync(stored.Value.CreatorId, cancellationToken);
        if (creator is null || creator.Value.Status != UserStatus.Active)
        {
            return Result<OneTimeLinkPayloadDto>.NotFound();
        }

        var link = stored.Value;
        if (await vault.GetSecretAsync(link.OwnerId, link.SecretId, cancellationToken) is null)
        {
            return Result<OneTimeLinkPayloadDto>.NotFound();
        }

        if (!await links.TryConsumeAsync(request.LinkId, stored.ETag, cancellationToken))
        {
            return Result<OneTimeLinkPayloadDto>.NotFound();
        }

        await audit.WriteCommittedAsync(new OneTimeLinkAuditItem(
            AuditEventType.OneTimeLinkOpened,
            AuditActors.AnonymousLinkRecipient,
            link.Id,
            link.OwnerId,
            link.SecretId,
            request.ClientIp,
            request.UserAgent));

        await CompleteAsync(link, now, cancellationToken);

        return Result<OneTimeLinkPayloadDto>.Success(new OneTimeLinkPayloadDto
        {
            Payload = Convert.ToBase64String(link.Payload),
        });
    }

    private async Task CompleteAsync(OneTimeLink link, DateTimeOffset openedAt, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(VaultLimits.CommittedAuditTimeout, timeProvider);
        var token = timeout.Token;

        OneTimeLinkIndexEntry? index = null;
        var indexRead = await RunStepAsync(ReadIndexStep, async () => index = await links.GetIndexEntryAsync(link.CreatorId, link.Id, token));

        if (index is null)
        {
            if (indexRead)
            {
                LogNoIndexRow(logger, link.Id);
            }
        }
        else
        {
            var notice = new OneTimeLinkNotice
            {
                LinkId = link.Id,
                CreatorId = link.CreatorId,
                OwnerId = link.OwnerId,
                SecretId = link.SecretId,
                NameEnvelope = index.NameEnvelope,
                MetadataKeyVersion = index.MetadataKeyVersion,
                OpenedAt = openedAt,
            };
            await RunStepAsync(AddNoticeStep, () => links.AddNoticeAsync(notice, token));
        }

        await RunStepAsync(RemoveBookkeepingStep, () => links.RemoveBookkeepingAsync(link.CreatorId, link.Id, link.ExpiresAt, token));
    }

    private async Task<bool> RunStepAsync(string step, Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception exception)
        {
            LogCleanupFailed(logger, step, exception.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "One-time link cleanup step '{Step}' did not finish ({ExceptionType})")]
    private static partial void LogCleanupFailed(ILogger logger, string step, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "One-time link {LinkId} has no index row, so no notice was written")]
    private static partial void LogNoIndexRow(ILogger logger, Guid linkId);
}
