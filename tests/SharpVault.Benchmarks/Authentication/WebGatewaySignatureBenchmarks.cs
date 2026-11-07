// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using BenchmarkDotNet.Attributes;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.Benchmarks.Authentication;

[MemoryDiagnoser]
public class WebGatewaySignatureBenchmarks
{
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Scope = "6f1c2b3a-0000-4000-8000-000000000001";
    private const string ClientIp = "203.0.113.50";
    private const long UnixSeconds = 1_800_000_000;

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(UnixSeconds);

    private string _signature = string.Empty;

    [GlobalSetup]
    public void Setup() =>
        _signature = WebGatewaySignature.Sign(Key, WebGatewayPurposes.ClientAddress, Scope, UnixSeconds, ClientIp, string.Empty);

    [Benchmark]
    public static string Sign() =>
        WebGatewaySignature.Sign(Key, WebGatewayPurposes.ClientAddress, Scope, UnixSeconds, ClientIp, string.Empty);

    [Benchmark]
    public bool Verify() =>
        WebGatewaySignature.Verify(Key, WebGatewayPurposes.ClientAddress, Scope, UnixSeconds, ClientIp, string.Empty, _signature, Now);
}
