// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace SharpVault.Benchmarks.Support;

internal sealed class BenchmarkHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "SharpVault.Benchmarks";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
