// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Queries;

public sealed record ListOneTimeLinksQuery(int PageSize, string? ContinuationToken) : IRequest<Result<OneTimeLinkPageDto>>;

public sealed partial class ListOneTimeLinksQueryValidator : AbstractValidator<ListOneTimeLinksQuery>
{
    private const string LinkIdPattern = @"^[0-9a-f]{32}\z";

    public ListOneTimeLinksQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, OneTimeLinkLimits.MaxPageSize);

        RuleFor(x => x.ContinuationToken)
            .Must(IsLinkIdToken)
            .When(x => x.ContinuationToken is not null)
            .WithMessage(OneTimeLinkMessages.InvalidContinuationToken);
    }

    private static bool IsLinkIdToken(string? token) => token is not null && LinkIdRegex().IsMatch(token);

    [GeneratedRegex(LinkIdPattern, RegexOptions.CultureInvariant)]
    private static partial Regex LinkIdRegex();
}

public sealed class ListOneTimeLinksQueryHandler(IOneTimeLinkStore links, VaultKeyRingService keyRings, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<ListOneTimeLinksQuery, Result<OneTimeLinkPageDto>>
{
    private const string LinkIdFormat = "N";

    public async Task<Result<OneTimeLinkPageDto>> Handle(ListOneTimeLinksQuery request, CancellationToken cancellationToken)
    {
        var after = request.ContinuationToken is null ? null : (Guid?)Guid.ParseExact(request.ContinuationToken, LinkIdFormat);
        var page = await links.ListIndexAsync(currentUser.UserId, timeProvider.GetUtcNow(), request.PageSize, after, cancellationToken);

        using var names = new OwnerVaultReads(keyRings);
        var items = new List<OneTimeLinkDto>(page.Items.Count);
        foreach (var entry in page.Items)
        {
            items.Add(await ToDtoAsync(entry, names, cancellationToken));
        }

        return Result<OneTimeLinkPageDto>.Success(new OneTimeLinkPageDto
        {
            Items = items,
            ContinuationToken = page.NextAfterLinkId?.ToString(LinkIdFormat, CultureInfo.InvariantCulture),
        });
    }

    private static async Task<OneTimeLinkDto> ToDtoAsync(OneTimeLinkIndexEntry entry, OwnerVaultReads names, CancellationToken cancellationToken) => new()
    {
        Id = entry.LinkId,
        OwnerId = entry.OwnerId,
        ItemId = entry.SecretId,
        ItemName = await names.TryNameAsync(entry.OwnerId, entry.SecretId, entry.NameEnvelope, entry.MetadataKeyVersion, cancellationToken),
        CreatedAt = entry.CreatedAt,
        ExpiresAt = entry.ExpiresAt,
    };
}
