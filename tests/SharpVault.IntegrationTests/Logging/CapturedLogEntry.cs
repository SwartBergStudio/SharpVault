// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;

namespace SharpVault.IntegrationTests.Logging;

public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, string? Exception);
