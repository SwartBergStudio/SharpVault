// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text;
using Microsoft.Playwright;

namespace SharpVault.E2ETests.Support;

public sealed class WebSocketFrames
{
    private readonly List<string> _sent = [];
    private readonly List<string> _received = [];
    private readonly Lock _gate = new();
    private int _sockets;

    public int SocketCount
    {
        get
        {
            lock (_gate)
            {
                return _sockets;
            }
        }
    }

    public int SentCount
    {
        get
        {
            lock (_gate)
            {
                return _sent.Count;
            }
        }
    }

    public void Attach(IWebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        lock (_gate)
        {
            _sockets++;
        }

        socket.FrameSent += (_, frame) => Record(_sent, frame);
        socket.FrameReceived += (_, frame) => Record(_received, frame);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _sent.Clear();
            _received.Clear();
        }
    }

    public void AssertNoFrameContains(string needle, string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(needle);
        string[] frames;
        lock (_gate)
        {
            frames = [.. _sent, .. _received];
        }

        Assert.NotEmpty(frames);
        var leaking = frames.Count(frame => frame.Contains(needle, StringComparison.Ordinal));
        Assert.True(leaking == 0, $"{leaking} WebSocket frame(s) carried {description}.");
    }

    private void Record(List<string> target, IWebSocketFrame frame)
    {
        var text = frame.Text ?? (frame.Binary is { } bytes ? Encoding.Latin1.GetString(bytes) : string.Empty);
        lock (_gate)
        {
            target.Add(text);
        }
    }
}
