using IntraTeams.Shared;

namespace IntraTeams.WinForms;

/// <summary>
/// The wrapper form's view of the call. This is the signaling-level state
/// machine; the CallControl separately tracks the media-level state.
/// </summary>
internal enum WrapperCallState
{
    Idle = 0,

    /// <summary>We invited someone and are waiting for them to pick up.</summary>
    Dialing = 1,

    /// <summary>Someone invited us; the incoming-call dialog is showing.</summary>
    Ringing = 2,

    /// <summary>Accepted; SDP / ICE being exchanged, media not flowing yet.</summary>
    Connecting = 3,

    /// <summary>Media connected.</summary>
    InCall = 4
}

/// <summary>One row in the people list.</summary>
internal sealed class PersonItem
{
    public PersonItem(UserInfo user)
    {
        ConnectionId = user.ConnectionId;
        DisplayName = user.DisplayName;
        Presence = user.Presence;
    }

    public string ConnectionId { get; }

    public string DisplayName { get; }

    public PresenceState Presence { get; }

    public bool IsAvailable => Presence == PresenceState.Available;

    public override string ToString() => DisplayName;
}

/// <summary>
/// A chat line as stored in a conversation. IsMine is captured when the
/// message arrives, because after a reconnect our own connection id changes and
/// could no longer be compared against.
/// </summary>
internal sealed record ChatLine(string Sender, string Text, DateTimeOffset SentAtUtc, bool IsMine);
