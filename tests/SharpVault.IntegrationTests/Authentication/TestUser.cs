// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Authentication;

public sealed record TestUser(Guid ObjectId, string DisplayName, Guid TenantId, IReadOnlyList<string> Roles);
