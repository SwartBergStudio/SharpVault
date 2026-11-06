// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using BenchmarkDotNet.Attributes;
using SharpVault.PasswordGeneration;

namespace SharpVault.Benchmarks.Vault;

[MemoryDiagnoser]
public class PasswordGenerationBenchmarks
{
    private const int DefaultLength = 20;
    private const int LargeLength = 128;
    private const int LetterSetSize = 26;
    private const int DigitSetSize = 10;
    private const int SymbolSetSize = 32;

    private readonly PasswordOptions _short = new(DefaultLength, CharacterSets.All, false, true);
    private readonly PasswordOptions _large = new(LargeLength, CharacterSets.All, false, true);
    private readonly PassphraseOptions _passphrase = PassphraseOptions.Default;
    private readonly int[] _setSizes = [LetterSetSize, LetterSetSize, DigitSetSize, SymbolSetSize];

    [Benchmark]
    public GeneratedSecret GeneratePassword20AllSets() => PasswordGenerator.GeneratePassword(_short);

    [Benchmark]
    public GeneratedSecret GeneratePassword128AllSets() => PasswordGenerator.GeneratePassword(_large);

    [Benchmark]
    public GeneratedSecret GeneratePassphrase6Words() => PasswordGenerator.GeneratePassphrase(_passphrase);

    [Benchmark]
    public double EntropyRequireEverySet128() => EntropyCalculator.PasswordBits(_setSizes, LargeLength, true);
}
