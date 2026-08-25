using System;

namespace IntraTeams.Shared
{
    /// <summary>Presence state shown next to a user in the people list.</summary>
    public enum PresenceState
    {
        Available = 0,
        InCall = 1
    }

    /// <summary>A connected user as advertised to every other client.</summary>
    public class UserInfo
    {
        /// <summary>SignalR connection id. This is the address used for all 1-1 routing.</summary>
        public string ConnectionId { get; set; }

        /// <summary>Windows account name, e.g. "ashok".</summary>
        public string UserName { get; set; }

        /// <summary>Machine the client is running on, used to disambiguate duplicate logins.</summary>
        public string MachineName { get; set; }

        /// <summary>What is rendered in the people list.</summary>
        public string DisplayName { get; set; }

        public PresenceState Presence { get; set; }

        public DateTimeOffset ConnectedAtUtc { get; set; }
    }

    /// <summary>Sent by the caller to ring a callee.</summary>
    public class CallInvite
    {
        /// <summary>Correlates every message belonging to one call attempt.</summary>
        public string CallId { get; set; }

        /// <summary>Connection id of the peer that initiated the call.</summary>
        public string FromConnectionId { get; set; }

        public string FromDisplayName { get; set; }

        /// <summary>True when the caller intends to send camera video from the start.</summary>
        public bool WithVideo { get; set; }
    }

    /// <summary>Carries an SDP offer or answer between the two peers.</summary>
    public class SdpMessage
    {
        public string CallId { get; set; }

        /// <summary>Set by the sender; overwritten by the server with the true caller id.</summary>
        public string FromConnectionId { get; set; }

        public string ToConnectionId { get; set; }

        /// <summary>"offer" or "answer" - matches RTCSdpType names used by SIPSorcery.</summary>
        public string Type { get; set; }

        public string Sdp { get; set; }
    }

    /// <summary>
    /// A single trickled ICE candidate. On an intranet these are host candidates only,
    /// because the client is configured with no STUN and no TURN servers.
    /// </summary>
    public class IceCandidateMessage
    {
        public string CallId { get; set; }

        public string FromConnectionId { get; set; }

        public string ToConnectionId { get; set; }

        public string Candidate { get; set; }

        public string SdpMid { get; set; }

        public int SdpMLineIndex { get; set; }

        public string UsernameFragment { get; set; }
    }

    /// <summary>Why a call stopped, so the far end can show a sensible message.</summary>
    public static class CallEndReasons
    {
        public const string HungUp = "hungup";
        public const string Rejected = "rejected";
        public const string Busy = "busy";
        public const string PeerDisconnected = "peer-disconnected";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
    }

    /// <summary>A 1-1 text message relayed over the same hub as the call signaling.</summary>
    public class ChatMessage
    {
        public string FromConnectionId { get; set; }

        public string FromDisplayName { get; set; }

        public string ToConnectionId { get; set; }

        public string Text { get; set; }

        public DateTimeOffset SentAtUtc { get; set; }
    }
}
