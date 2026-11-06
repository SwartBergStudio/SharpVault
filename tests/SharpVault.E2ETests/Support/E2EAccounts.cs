// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.E2ETests.Support;

public static class E2EAccounts
{
    public const string AdminOne = "Admin One";
    public const string AdminTwo = "Admin Two";
    public const string UserOne = "User One";
    public const string UserTwo = "User Two";
    public static readonly Guid AdminOneId = Guid.Parse("5a1d0000-0000-4000-8000-000000000001");
    public static readonly Guid AdminTwoId = Guid.Parse("5a1d0000-0000-4000-8000-000000000002");
    public static readonly Guid UserOneId = Guid.Parse("5a1d0000-0000-4000-8000-000000000003");
    public static readonly Guid UserTwoId = Guid.Parse("5a1d0000-0000-4000-8000-000000000004");
}
