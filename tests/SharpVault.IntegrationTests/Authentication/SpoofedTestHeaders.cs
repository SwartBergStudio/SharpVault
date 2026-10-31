// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net.Http.Headers;

namespace SharpVault.IntegrationTests.Authentication;

public static class SpoofedTestHeaders
{
    public const string ObjectIdHeader = "X-Test-Oid";
    public const string NameHeader = "X-Test-Name";
    public const string TenantHeader = "X-Test-Tid";
    public const string RolesHeader = "X-Test-Roles";

    public static void Add(HttpRequestHeaders headers, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(user);

        headers.Remove(ObjectIdHeader);
        headers.Remove(NameHeader);
        headers.Remove(TenantHeader);
        headers.Remove(RolesHeader);
        headers.Add(ObjectIdHeader, user.ObjectId.ToString());
        headers.Add(NameHeader, user.DisplayName);
        headers.Add(TenantHeader, user.TenantId.ToString());
        headers.Add(RolesHeader, string.Join(',', user.Roles));
    }
}
