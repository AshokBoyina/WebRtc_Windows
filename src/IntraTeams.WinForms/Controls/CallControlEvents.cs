namespace IntraTeams.WinForms.Controls;

/// <summary>Media-level state of a <see cref="CallControl"/>.</summary>
public enum CallControlState
{
    /// <summary>No session. The control is empty and its buttons are disabled.</summary>
    Idle = 0,

    /// <summary>A session exists; SDP / ICE are being exchanged.</summary>
    Connecting = 1,

    /// <summary>The peer connection is up and media is flowing.</summary>
    Connected = 2,

    /// <summary>
    /// Negotiation or the media path failed. The session is still allocated:
    /// the host decides what to tell the peer and then calls
    /// <see cref="CallControl.EndCallAsync"/>.
    /// </summary>
    Failed = 3
}

/// <summary>An SDP offer or answer the host must deliver to the remote peer.</summary>
public sealed class LocalSdpEventArgs : EventArgs
{
    public LocalSdpEventArgs(string type, string sdp)
    {
        Type = type;
        Sdp = sdp;
    }

    /// <summary>"offer" or "answer".</summary>
    public string Type { get; }

    public string Sdp { get; }
}

/// <summary>A locally gathered ICE candidate the host must deliver to the remote peer.</summary>
public sealed class LocalIceCandidateEventArgs : EventArgs
{
    public LocalIceCandidateEventArgs(string candidate, string? sdpMid, int sdpMLineIndex, string? usernameFragment)
    {
        Candidate = candidate;
        SdpMid = sdpMid;
        SdpMLineIndex = sdpMLineIndex;
        UsernameFragment = usernameFragment;
    }

    public string Candidate { get; }

    public string? SdpMid { get; }

    public int SdpMLineIndex { get; }

    public string? UsernameFragment { get; }
}

public sealed class CallStateChangedEventArgs : EventArgs
{
    public CallStateChangedEventArgs(CallControlState state, string detail)
    {
        State = state;
        Detail = detail;
    }

    public CallControlState State { get; }

    /// <summary>Human-readable description, suitable for a status bar.</summary>
    public string Detail { get; }
}

public sealed class DiagnosticEventArgs : EventArgs
{
    public DiagnosticEventArgs(string message) => Message = message;

    public string Message { get; }
}
