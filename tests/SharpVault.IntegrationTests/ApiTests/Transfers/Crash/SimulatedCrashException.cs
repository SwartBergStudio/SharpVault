// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.ApiTests.Transfers.Crash;

public sealed class SimulatedCrashException(string point) : Exception("Simulated crash at " + point + ".")
{
    public string Point { get; } = point;
}
