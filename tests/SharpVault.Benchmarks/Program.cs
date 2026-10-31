// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using BenchmarkDotNet.Running;

const int SuccessExitCode = 0;
const int FailureExitCode = 1;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

return summaries.Any(summary => summary.HasCriticalValidationErrors || summary.Reports.Any(report => !report.Success))
    ? FailureExitCode
    : SuccessExitCode;
