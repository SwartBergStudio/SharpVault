// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Components.Common;

public sealed class StatusMessage
{
    public string? Text { get; private set; }

    public AlertKind Kind { get; private set; }

    public bool Expires { get; private set; }

    public int Version { get; private set; }

    public bool IsFailure => Text is not null && Kind == AlertKind.Danger;

    public void Show(string text, AlertKind kind, bool expires = false)
    {
        Text = text;
        Kind = kind;
        Expires = expires;
        Version++;
    }

    public void Clear()
    {
        Text = null;
        Expires = false;
        Version++;
    }

    public void ClearFailure()
    {
        if (IsFailure)
        {
            Clear();
        }
    }
}
