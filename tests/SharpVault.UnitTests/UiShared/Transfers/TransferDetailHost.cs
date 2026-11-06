// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SharpVault.UI.Shared.Components.Transfers;

namespace SharpVault.UnitTests.UiShared.Transfers;

public sealed class TransferDetailHost : ComponentBase
{
    [Parameter]
    public bool Show { get; set; } = true;

    [Parameter]
    public Guid TransferId { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!Show)
        {
            return;
        }

        builder.OpenComponent<TransferDetail>(0);
        builder.AddAttribute(1, nameof(TransferDetail.TransferId), TransferId);
        builder.CloseComponent();
    }
}
