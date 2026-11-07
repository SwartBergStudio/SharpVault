// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.OneTimeLinks;

namespace SharpVault.Web.OneTimeLinks;

public sealed record OneTimeLinkOpenResult(OneTimeLinkOpenOutcome Outcome, OneTimeLinkPayloadDto? Payload = null, TimeSpan? RetryAfter = null)
{
    public static OneTimeLinkOpenResult Unavailable => new(OneTimeLinkOpenOutcome.Unavailable);
}
