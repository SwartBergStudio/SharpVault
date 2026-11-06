// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using FluentValidation.Results;
using SharpVault.Application.Features.Users.Common;

namespace SharpVault.UnitTests.Support;

public static class ServerReasonRules
{
    private static readonly ReasonProbeValidator Validator = new();

    public static ValidationResult Validate(string? reason) => Validator.Validate(new ReasonProbe(reason!));

    public static bool Accepts(string? reason) => Validate(reason).IsValid;

    private sealed record ReasonProbe(string Reason);

    private sealed class ReasonProbeValidator : AbstractValidator<ReasonProbe>
    {
        public ReasonProbeValidator() => RuleFor(probe => probe.Reason).AdminReason();
    }
}
