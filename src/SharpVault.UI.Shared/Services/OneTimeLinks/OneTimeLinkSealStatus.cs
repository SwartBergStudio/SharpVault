// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.OneTimeLinks;

public enum OneTimeLinkSealStatus
{
    Sealed = 0,
    PassphraseTooShort,
    Unsupported,
}
