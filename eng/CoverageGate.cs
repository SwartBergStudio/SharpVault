// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Xml.Linq;

const int ExitSuccess = 0;
const int ExitBelowThreshold = 1;
const int ExitUsageError = 2;
const char ThresholdSeparator = '=';

if (args.Length < 2)
{
    return PrintUsage();
}

var reportPath = args[0];
if (!File.Exists(reportPath))
{
    return PrintUsage();
}

var thresholds = new List<(string Prefix, double Percent)>();
foreach (var argument in args.Skip(1))
{
    var separatorIndex = argument.LastIndexOf(ThresholdSeparator);
    if (separatorIndex <= 0
        || !double.TryParse(
            argument[(separatorIndex + 1)..],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var percent)
        || percent < 0)
    {
        return PrintUsage();
    }

    thresholds.Add((argument[..separatorIndex], percent));
}

XDocument report;
try
{
    report = XDocument.Load(reportPath);
}
catch (System.Xml.XmlException)
{
    return PrintUsage();
}

var classes = report.Descendants("class").ToList();
var belowThreshold = false;

foreach (var (prefix, threshold) in thresholds)
{
    var lines = classes
        .Where(element =>
        {
            var name = (string?)element.Attribute("name") ?? string.Empty;
            return name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal);
        })
        .SelectMany(element => element.Elements("lines").Elements("line")
            .Select(line => (
                File: (string?)element.Attribute("filename") ?? string.Empty,
                Number: (string?)line.Attribute("number") ?? string.Empty,
                Covered: ((int?)line.Attribute("hits") ?? 0) > 0)))
        .GroupBy(line => (line.File, line.Number))
        .Select(group => group.Any(line => line.Covered))
        .ToList();

    if (lines.Count == 0)
    {
        Console.WriteLine($"{prefix}: no coverable lines found");
        belowThreshold = true;
        continue;
    }

    var percent = lines.Count(covered => covered) * 100.0 / lines.Count;
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"{prefix}: {percent:F1}% line coverage (threshold {threshold}%)"));

    if (percent < threshold)
    {
        belowThreshold = true;
    }
}

return belowThreshold ? ExitBelowThreshold : ExitSuccess;

static int PrintUsage()
{
    Console.Error.WriteLine("Usage: dotnet run eng/CoverageGate.cs -- <cobertura.xml> <Prefix>=<Percent> [<Prefix>=<Percent> ...]");
    return ExitUsageError;
}
