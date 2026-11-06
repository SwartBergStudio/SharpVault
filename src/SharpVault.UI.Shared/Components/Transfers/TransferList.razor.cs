// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Web;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services.Users;

namespace SharpVault.UI.Shared.Components.Transfers;

public sealed partial class TransferList
{
    public const int PageSize = 25;


    private const string LoadMoreFailedText = "More transfers could not be loaded. Try again.";

    private readonly List<TransferSummaryDto> _rows = [];
    private string? _continuation;
    private bool _forbidden;

    protected override async Task<ApiResponse<TransferPageDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var current = await CurrentUserAccessor.GetAsync(cancellationToken);
        if (current.Status != ApiCallStatus.Success || current.Value is null)
        {
            return new ApiResponse<TransferPageDto>(current.Status, null);
        }

        _forbidden = !current.Value.IsAdmin;
        if (_forbidden)
        {
            return ApiResponse<TransferPageDto>.Success(new TransferPageDto());
        }

        var response = await Transfers.ListAsync(PageSize, null, cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _rows.Clear();
            _rows.AddRange(response.Value.Items);
            _continuation = response.Value.ContinuationToken;
            LoadMoreFailed = false;
        }

        return response;
    }

    protected override bool IsEmpty(TransferPageDto value) =>
        !_forbidden && value.Items.Count == 0 && value.ContinuationToken is null;

    protected override void OnAccessDenied()
    {
        _rows.Clear();
        _continuation = null;
        base.OnAccessDenied();
    }

    private static string RequestedText(TransferSummaryDto transfer) =>
        StampText.For(transfer.RequestedAt);

    private Task HandleRefresh(MouseEventArgs args) => ReloadAsync();

    private Task HandleLoadMore(MouseEventArgs args) =>
        _continuation is null
            ? Task.CompletedTask
            : RunLoadMoreAsync(token => Transfers.ListAsync(PageSize, _continuation, token), AppendPage, LoadMoreFailedText);

    private void AppendPage(TransferPageDto page)
    {
        _rows.AddRange(page.Items);
        _continuation = page.ContinuationToken;
    }
}
