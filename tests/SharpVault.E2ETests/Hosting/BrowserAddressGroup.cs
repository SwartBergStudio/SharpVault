// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.E2ETests.Hosting;

namespace SharpVault.E2ETests;

[CollectionDefinition(nameof(BrowserAddressGroup))]
public sealed class BrowserAddressGroup : ICollectionFixture<BrowserAddressHost>;
