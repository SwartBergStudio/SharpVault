// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Encryption;

public sealed record EncryptionContext(string Purpose, Guid ItemId, Guid OwnerId);
