namespace IntraTeams.Shared
{
    /// <summary>
    /// Names of the hub methods the CLIENT invokes on the SERVER.
    /// Kept as constants so the client and server can never drift apart.
    /// </summary>
    public static class HubMethods
    {
        public const string Register = nameof(Register);
        public const string InviteCall = nameof(InviteCall);
        public const string AcceptCall = nameof(AcceptCall);
        public const string RejectCall = nameof(RejectCall);
        public const string EndCall = nameof(EndCall);
        public const string SendSdp = nameof(SendSdp);
        public const string SendIceCandidate = nameof(SendIceCandidate);
        public const string SendChat = nameof(SendChat);
    }

    /// <summary>
    /// Names of the callback methods the SERVER invokes on the CLIENT.
    /// </summary>
    public static class ClientMethods
    {
        public const string RosterChanged = nameof(RosterChanged);
        public const string IncomingCall = nameof(IncomingCall);
        public const string CallAccepted = nameof(CallAccepted);
        public const string CallRejected = nameof(CallRejected);
        public const string CallEnded = nameof(CallEnded);
        public const string ReceiveSdp = nameof(ReceiveSdp);
        public const string ReceiveIceCandidate = nameof(ReceiveIceCandidate);
        public const string ReceiveChat = nameof(ReceiveChat);
    }

    /// <summary>Well-known route the signaling hub is mapped to.</summary>
    public static class HubRoutes
    {
        public const string Signaling = "/hubs/signaling";
    }
}
