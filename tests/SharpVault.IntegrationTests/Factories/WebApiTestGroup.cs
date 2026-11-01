// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Factories;

[CollectionDefinition(nameof(WebApiTestGroup))]
public sealed class WebApiTestGroup : ICollectionFixture<ApiTestFactory>, ICollectionFixture<WebTestFactory>;
