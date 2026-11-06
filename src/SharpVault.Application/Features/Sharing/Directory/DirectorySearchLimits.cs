// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Sharing.Directory;

public static class DirectorySearchLimits
{
    public const int ResultLimit = 10;
    public const int StatusParallelism = 4;
    public const int MinTermLength = 3;
    public const int MaxTermLength = 100;
    public const int MaxRowsScanned = 1000;
}
