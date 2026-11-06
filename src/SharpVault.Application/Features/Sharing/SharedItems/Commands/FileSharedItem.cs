// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Sharing.SharedItems.Commands;

public sealed record FileSharedItemCommand(Guid OwnerId, ShareItemKind Kind, Guid ItemId, Guid? FolderId) : IRequest<Result<Done>>;

public sealed class FileSharedItemCommandValidator : AbstractValidator<FileSharedItemCommand>
{
    public FileSharedItemCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.ItemId).NotEqual(Guid.Empty);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.FolderId).NotEqual(Guid.Empty).When(x => x.FolderId is not null);
    }
}

public sealed class FileSharedItemCommandHandler(
    IShareInboxStore inbox,
    IVaultStore store,
    ICurrentUser currentUser,
    ILogger<FileSharedItemCommandHandler> logger) : IRequestHandler<FileSharedItemCommand, Result<Done>>
{
    public async Task<Result<Done>> Handle(FileSharedItemCommand request, CancellationToken cancellationToken)
    {
        var caller = currentUser.UserId;
        if (request.OwnerId == caller)
        {
            return Result<Done>.NotFound();
        }

        var entryTask = inbox.GetAsync(caller, request.OwnerId, request.Kind, request.ItemId, cancellationToken);
        var grantTask = store.GetShareGrantAsync(request.OwnerId, request.Kind, request.ItemId, caller, cancellationToken);
        var folderTask = request.FolderId is { } folderId
            ? store.GetFolderAsync(caller, folderId, cancellationToken)
            : Task.FromResult<Versioned<Folder>?>(null);
        await Task.WhenAll(entryTask, grantTask, folderTask);

        var entry = await entryTask;
        if (entry is null || await grantTask is null || (request.FolderId is not null && await folderTask is null))
        {
            return Result<Done>.NotFound();
        }

        entry.Value.FiledFolderId = request.FolderId;
        var result = await inbox.SetFiledFolderAsync(entry.Value, entry.ETag, cancellationToken);
        return VaultWrites.ToFailure<Done>(result, logger, request.ItemId) ?? Result<Done>.Success(Done.Value);
    }
}
