// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text;

namespace SharpVault.E2ETests.Hosting;

public sealed class ProcessLog
{
    private readonly StringBuilder _text = new();
    private readonly Lock _gate = new();

    public void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_gate)
        {
            _text.AppendLine(line);
        }
    }

    public override string ToString()
    {
        lock (_gate)
        {
            return _text.ToString();
        }
    }
}
