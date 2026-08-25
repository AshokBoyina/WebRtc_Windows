using System.Collections.Concurrent;
using IntraTeams.Shared;

namespace IntraTeams.Server.Services;

/// <summary>
/// In-memory roster of connected clients.
///
/// A POC deliberately keeps this in process memory: it is rebuilt from scratch
/// whenever the server restarts, and every client re-registers on reconnect.
/// If you later scale to more than one server instance you must replace this
/// with a backplane (Redis) or the roster will be per-instance.
/// </summary>
public sealed class UserRegistry
{
    private readonly ConcurrentDictionary<string, UserInfo> _users = new();

    /// <summary>Tracks who is talking to whom, keyed by connection id (both directions).</summary>
    private readonly ConcurrentDictionary<string, string> _activeCallPartners = new();

    public UserInfo Add(string connectionId, string userName, string machineName)
    {
        var display = string.IsNullOrWhiteSpace(machineName)
            ? userName
            : $"{userName} ({machineName})";

        var user = new UserInfo
        {
            ConnectionId = connectionId,
            UserName = userName,
            MachineName = machineName,
            DisplayName = display,
            Presence = PresenceState.Available,
            ConnectedAtUtc = DateTimeOffset.UtcNow
        };

        _users[connectionId] = user;
        return user;
    }

    public UserInfo? Remove(string connectionId)
    {
        _activeCallPartners.TryRemove(connectionId, out _);
        return _users.TryRemove(connectionId, out var user) ? user : null;
    }

    public UserInfo? Get(string connectionId)
        => _users.TryGetValue(connectionId, out var user) ? user : null;

    public IReadOnlyList<UserInfo> Snapshot()
        => _users.Values.OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Marks both peers as busy and records the pairing.</summary>
    public void BeginCall(string a, string b)
    {
        _activeCallPartners[a] = b;
        _activeCallPartners[b] = a;
        SetPresence(a, PresenceState.InCall);
        SetPresence(b, PresenceState.InCall);
    }

    /// <summary>Clears the pairing for a connection and returns the partner it was talking to.</summary>
    public string? EndCall(string connectionId)
    {
        if (!_activeCallPartners.TryRemove(connectionId, out var partner))
        {
            SetPresence(connectionId, PresenceState.Available);
            return null;
        }

        _activeCallPartners.TryRemove(partner, out _);
        SetPresence(connectionId, PresenceState.Available);
        SetPresence(partner, PresenceState.Available);
        return partner;
    }

    public string? GetCallPartner(string connectionId)
        => _activeCallPartners.TryGetValue(connectionId, out var partner) ? partner : null;

    public bool IsBusy(string connectionId)
        => _activeCallPartners.ContainsKey(connectionId);

    private void SetPresence(string connectionId, PresenceState state)
    {
        if (_users.TryGetValue(connectionId, out var user))
        {
            user.Presence = state;
        }
    }
}
