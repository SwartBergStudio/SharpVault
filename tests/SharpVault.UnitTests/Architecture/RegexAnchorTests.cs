// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text;
using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

[Trait("Category", "Security")]
public sealed partial class RegexAnchorTests
{
    [Fact]
    public void SourceFiles_Regexes_DoNotUseDollarEndAnchor()
    {
        var sourceRoot = Path.Combine(RepositoryRoot.Path, "src");
        var files = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(path))
            .ToList();

        var offenders = files
            .SelectMany(path => RegexPatternScanner.FindDollarAnchored(File.ReadAllText(path))
                .Select(hit => $"{Path.GetRelativePath(RepositoryRoot.Path, path)}:{hit.Line} pattern \"{hit.Pattern}\""))
            .ToList();

        Assert.NotEmpty(files);
        Assert.True(offenders.Count == 0, "Regex patterns must end with \\z, not $: " + string.Join("; ", offenders));
    }

    [Theory]
    [InlineData("[GeneratedRegex(\"^a$\")]")]
    [InlineData("[GeneratedRegex(@\"^a$\")]")]
    [InlineData("var r = new Regex(\"^a$\");")]
    [InlineData("var r = new Regex(@\"^a$\", RegexOptions.None);")]
    [InlineData("var ok = Regex.IsMatch(value, \"^a$\");")]
    [InlineData("var ok = Regex.IsMatch(value, @\"^a$\");")]
    [InlineData("var m = Regex.Match(value, @\"^a$\");")]
    [InlineData("var m = Regex.Matches(value, \"^a$\");")]
    [InlineData("var m = Regex.Replace(value, \"^a$\", \"b\");")]
    [InlineData("[GeneratedRegex(\"^(a|b$)\")]")]
    [InlineData("[GeneratedRegex(\"^a\\\\\\\\$\")]")]
    public void Scanner_DollarAnchoredPattern_IsDetected(string source)
    {
        var hits = RegexPatternScanner.FindDollarAnchored(source);

        Assert.Single(hits);
    }

    [Theory]
    [InlineData("[GeneratedRegex(\"^a\\\\z\")]")]
    [InlineData("[GeneratedRegex(@\"^a\\z\")]")]
    [InlineData("[GeneratedRegex(\"^a\\\\$b\\\\z\")]")]
    [InlineData("[GeneratedRegex(\"^a\\\\$\")]")]
    [InlineData("[GeneratedRegex(@\"^a\\$\")]")]
    [InlineData("var r = new Regex(\"^\\\\$(a)\\\\z\");")]
    [InlineData("var text = \"^a$\";")]
    public void Scanner_SafePattern_IsAccepted(string source)
    {
        var hits = RegexPatternScanner.FindDollarAnchored(source);

        Assert.Empty(hits);
    }

    [Fact]
    public void Scanner_MultiLineSource_ReportsLineNumber()
    {
        var source = "class A\n{\n    [GeneratedRegex(\"^ok\\\\z\")]\n    static partial Regex Ok();\n\n    [GeneratedRegex(\"^bad$\")]\n    static partial Regex Bad();\n}\n";

        var hits = RegexPatternScanner.FindDollarAnchored(source);

        var hit = Assert.Single(hits);
        Assert.Equal(6, hit.Line);
        Assert.Equal("^bad$", hit.Pattern);
    }

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains(separator + "obj" + separator, StringComparison.Ordinal)
            || path.Contains(separator + "bin" + separator, StringComparison.Ordinal);
    }

    internal readonly record struct PatternHit(int Line, string Pattern);

    internal static partial class RegexPatternScanner
    {
        public static IReadOnlyList<PatternHit> FindDollarAnchored(string source)
        {
            var hits = new List<PatternHit>();
            foreach (Match marker in RegexCallMarker().Matches(source))
            {
                var literal = ReadFirstLiteral(source, marker.Index + marker.Length);
                if (literal is null || !EndsWithUnescapedDollar(literal))
                {
                    continue;
                }

                hits.Add(new PatternHit(LineOf(source, marker.Index), literal));
            }

            return hits;
        }

        [GeneratedRegex(@"GeneratedRegex\s*\(|new\s+Regex\s*\(|Regex\s*\.\s*(?:IsMatch|Match|Matches|Replace)\s*\(")]
        private static partial Regex RegexCallMarker();

        private static bool EndsWithUnescapedDollar(string pattern)
        {
            for (var index = 0; index < pattern.Length; index++)
            {
                if (pattern[index] == '\\')
                {
                    index++;
                    continue;
                }

                if (pattern[index] == '$' && (index == pattern.Length - 1 || pattern[index + 1] == ')'))
                {
                    return true;
                }
            }

            return false;
        }

        private static int LineOf(string source, int index)
        {
            var line = 1;
            for (var position = 0; position < index; position++)
            {
                if (source[position] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static string? ReadFirstLiteral(string source, int start)
        {
            var depth = 1;
            for (var index = start; index < source.Length; index++)
            {
                var character = source[index];
                if (character == '"')
                {
                    var verbatim = index > 0 && (source[index - 1] == '@' || (index > 1 && source[index - 1] == '$' && source[index - 2] == '@'));
                    return verbatim ? ReadVerbatim(source, index + 1) : ReadRegular(source, index + 1);
                }

                if (character == '(')
                {
                    depth++;
                }
                else if (character == ')' && --depth == 0)
                {
                    return null;
                }
                else if (character == ';')
                {
                    return null;
                }
            }

            return null;
        }

        private static string ReadVerbatim(string source, int start)
        {
            var builder = new StringBuilder();
            for (var index = start; index < source.Length; index++)
            {
                if (source[index] == '"')
                {
                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        builder.Append('"');
                        index++;
                        continue;
                    }

                    break;
                }

                builder.Append(source[index]);
            }

            return builder.ToString();
        }

        private static string ReadRegular(string source, int start)
        {
            var builder = new StringBuilder();
            for (var index = start; index < source.Length; index++)
            {
                var character = source[index];
                if (character == '"')
                {
                    break;
                }

                if (character == '\\' && index + 1 < source.Length)
                {
                    var next = source[index + 1];
                    builder.Append(next is '\\' or '"' ? next : character);
                    index += next is '\\' or '"' ? 1 : 0;
                    continue;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }
    }
}
