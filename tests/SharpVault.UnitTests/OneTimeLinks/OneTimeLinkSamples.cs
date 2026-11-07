// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Domain.Users;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

internal static class OneTimeLinkSamples
{
    public static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    public static byte[] PayloadBytes(int length = 64)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        bytes[0] |= 0x80;
        return bytes;
    }

    public static string PayloadBase64(int length = 64) => Convert.ToBase64String(PayloadBytes(length));

    public static OneTimeLink NewLink(Guid creatorId, Guid ownerId, Guid secretId, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        CreatorId = creatorId,
        OwnerId = ownerId,
        SecretId = secretId,
        Payload = PayloadBytes(),
        CreatedAt = Now,
        ExpiresAt = expiresAt,
    };

    public static OneTimeLinkIndexEntry IndexFor(OneTimeLink link, byte[] nameEnvelope) => new()
    {
        LinkId = link.Id,
        CreatorId = link.CreatorId,
        OwnerId = link.OwnerId,
        SecretId = link.SecretId,
        NameEnvelope = nameEnvelope,
        MetadataKeyVersion = VaultLimits.CurrentMetadataKeyVersion,
        CreatedAt = link.CreatedAt,
        ExpiresAt = link.ExpiresAt,
    };

    public static OneTimeLinkIndexPage Page(params OneTimeLinkIndexEntry[] entries) => new(entries, null);

    [SuppressMessage("Usage", "CA2012", Justification = "The ValueTask is consumed by NSubstitute as the configured return value, not awaited by the test.")]
    public static IOneTimeLinkPolicy Policy(bool enabled = true, TimeSpan? maxLifetime = null)
    {
        var policy = Substitute.For<IOneTimeLinkPolicy>();
        policy.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new OneTimeLinkSettings(enabled, maxLifetime ?? TimeSpan.FromDays(7))));
        return policy;
    }

    public static Versioned<User> UserRow(Guid id, UserStatus status, bool isAdmin = false) => new(
        new User
        {
            Id = id,
            DisplayName = "Test user",
            Status = status,
            IsAdmin = isAdmin,
            CreatedAt = Now,
            LastSignInAt = Now,
        },
        "user-etag");

    public static OneTimeLinkAuditWriter AuditWriter(RecordingAuditLog log, TimeProvider time) =>
        new(log, time, NullLogger<OneTimeLinkAuditWriter>.Instance);
}
