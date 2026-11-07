// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Sharing.SharedItems.Queries;
using SharpVault.Application.Features.Transfers.Queries;
using SharpVault.Application.Features.Users.Queries;
using SharpVault.Application.Features.Vault.RecycleBin.Queries;
using SharpVault.Application.Features.Vault.Secrets.Queries;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Transfers;
using SharpVault.UI.Shared.Components.Users;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services.OneTimeLinks;

namespace SharpVault.UnitTests.UiShared.Contracts;

[Trait("Category", "Security")]
public sealed class PageSizeContractTests
{
    private static readonly Dictionary<string, int> ApiLimits = new(StringComparer.Ordinal)
    {
        [$"{nameof(SecretListPanel)}.{nameof(SecretListPanel.BrowsePageSize)}"] = ListSecretsQuery.MaxPageSize,
        [$"{nameof(SecretListPanel)}.{nameof(SecretListPanel.SearchPageSize)}"] = ListSecretsQuery.MaxPageSize,
        [$"{nameof(RecycleBinView)}.{nameof(RecycleBinView.PageSize)}"] = ListRecycleBinQuery.MaxPageSize,
        [$"{nameof(UserDirectoryTable)}.{nameof(UserDirectoryTable.PageSize)}"] = ListUsersQuery.MaxPageSize,
        [$"{nameof(TransferList)}.{nameof(TransferList.PageSize)}"] = ListTransfersQuery.MaxPageSize,
        [$"{nameof(TransferRequestForm)}.{nameof(TransferRequestForm.UserPageSize)}"] = ListUsersQuery.MaxPageSize,
        [$"{nameof(TransferSharesReview)}.{nameof(TransferSharesReview.PageSize)}"] = SharingLimits.MaxPageSize,
        [$"{nameof(SharedWithMeView)}.{nameof(SharedWithMeView.PageSize)}"] = ListSharedItemsQuery.MaxPageSize,
        [$"{nameof(SharedFiledPanel)}.{nameof(SharedFiledPanel.PageSize)}"] = ListSharedItemsQuery.MaxPageSize,
        [$"{nameof(SharedFolderView)}.{nameof(SharedFolderView.PageSize)}"] = SharingLimits.MaxPageSize,
        [$"{nameof(OneTimeLinkUiLimits)}.{nameof(OneTimeLinkUiLimits.PageSize)}"] = OneTimeLinkLimits.MaxPageSize,
    };

    private static List<(string Key, int Value)> UiPageSizeConstants() =>
        typeof(SharedWithMeView).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(field => field.IsLiteral && field.FieldType == typeof(int) && field.Name.Contains("PageSize", StringComparison.Ordinal))
                .Select(field => ($"{type.Name}.{field.Name}", (int)field.GetRawConstantValue()!)))
            .ToList();

    [Fact]
    public void EveryUiPageSizeConstant_IsMappedToAnApiLimit()
    {
        var unmapped = UiPageSizeConstants().Select(constant => constant.Key).Except(ApiLimits.Keys).ToList();

        Assert.Empty(unmapped);
    }

    [Fact]
    public void EveryMappedLimit_ExistsInTheUi()
    {
        var present = UiPageSizeConstants().Select(constant => constant.Key).ToList();

        Assert.Empty(ApiLimits.Keys.Except(present));
    }

    [Fact]
    public void EveryUiPageSizeConstant_IsWithinTheApiLimitOfItsEndpoint()
    {
        var offenders = UiPageSizeConstants()
            .Where(constant => ApiLimits.TryGetValue(constant.Key, out var limit) && (constant.Value < 1 || constant.Value > limit))
            .Select(constant => $"{constant.Key}={constant.Value} (limit {ApiLimits[constant.Key]})")
            .ToList();

        Assert.Empty(offenders);
    }
}
