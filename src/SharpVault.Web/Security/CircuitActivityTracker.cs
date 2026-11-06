// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web.Security;

public sealed class CircuitActivityTracker(SessionActivityMap sessions)
{
    private string? _sessionId;
    private DateTimeOffset _expiresAt;

    public TimeSpan IdleFor => ReadSessionId() is { } sessionId ? sessions.IdleFor(sessionId) : TimeSpan.MaxValue;

    public void Bind(string sessionId, DateTimeOffset expiresAt)
    {
        _expiresAt = expiresAt;
        Volatile.Write(ref _sessionId, sessionId);
        sessions.Start(sessionId, expiresAt);
    }

    public void Touch()
    {
        if (ReadSessionId() is { } sessionId)
        {
            sessions.Touch(sessionId, _expiresAt);
        }
    }

    private string? ReadSessionId() => Volatile.Read(ref _sessionId);
}
