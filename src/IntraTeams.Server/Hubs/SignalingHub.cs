using IntraTeams.Server.Services;
using IntraTeams.Shared;
using Microsoft.AspNetCore.SignalR;

namespace IntraTeams.Server.Hubs;

/// <summary>
/// Presence + signaling hub.
///
/// This hub never touches media. Its only jobs are:
///   1. maintain the roster of connected users,
///   2. ring / accept / reject / hang up,
///   3. relay SDP and ICE candidates verbatim between exactly two peers,
///   4. relay 1-1 text chat.
///
/// Once the peer connection is established the audio, video and screen RTP
/// flows directly between the two machines and never comes back through here.
/// </summary>
public sealed class SignalingHub : Hub
{
    private readonly UserRegistry _registry;
    private readonly ILogger<SignalingHub> _log;

    public SignalingHub(UserRegistry registry, ILogger<SignalingHub> log)
    {
        _registry = registry;
        _log = log;
    }

    // ---------------------------------------------------------------- roster

    /// <summary>
    /// First call a client makes after connecting. Returns the current roster
    /// and broadcasts the updated roster to everyone else.
    /// </summary>
    public async Task<IReadOnlyList<UserInfo>> Register(string userName, string machineName)
    {
        var user = _registry.Add(Context.ConnectionId, userName, machineName);
        _log.LogInformation("Registered {Display} as {ConnectionId}", user.DisplayName, user.ConnectionId);

        await BroadcastRosterAsync();
        return _registry.Snapshot();
    }

    public override async Task OnConnectedAsync()
    {
        _log.LogInformation("Connected {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var me = Context.ConnectionId;

        // If this user was mid-call, tell the far end so its UI does not hang
        // on a call that can never complete.
        var partner = _registry.EndCall(me);
        if (partner is not null)
        {
            await Clients.Client(partner)
                .SendAsync(ClientMethods.CallEnded, me, CallEndReasons.PeerDisconnected);
        }

        var removed = _registry.Remove(me);
        if (removed is not null)
        {
            _log.LogInformation("Disconnected {Display}", removed.DisplayName);
        }

        await BroadcastRosterAsync();
        await base.OnDisconnectedAsync(exception);
    }

    private Task BroadcastRosterAsync()
        => Clients.All.SendAsync(ClientMethods.RosterChanged, _registry.Snapshot());

    // ----------------------------------------------------------- call control

    /// <summary>Ring a peer. Fails soft if the peer is gone or already busy.</summary>
    public async Task InviteCall(string targetConnectionId, string callId, bool withVideo)
    {
        var me = _registry.Get(Context.ConnectionId);
        if (me is null)
        {
            return;
        }

        var target = _registry.Get(targetConnectionId);
        if (target is null)
        {
            await Clients.Caller.SendAsync(
                ClientMethods.CallEnded, targetConnectionId, CallEndReasons.PeerDisconnected);
            return;
        }

        if (_registry.IsBusy(targetConnectionId))
        {
            await Clients.Caller.SendAsync(
                ClientMethods.CallRejected, targetConnectionId, CallEndReasons.Busy);
            return;
        }

        _log.LogInformation("{From} is calling {To} (video={Video})",
            me.DisplayName, target.DisplayName, withVideo);

        await Clients.Client(targetConnectionId).SendAsync(ClientMethods.IncomingCall, new CallInvite
        {
            CallId = callId,
            FromConnectionId = Context.ConnectionId,
            FromDisplayName = me.DisplayName,
            WithVideo = withVideo
        });
    }

    /// <summary>
    /// Callee accepted. The two peers are now marked busy and the CALLER is
    /// expected to create and send the SDP offer. Keeping offer creation on one
    /// side only is what stops this POC from needing glare/rollback handling.
    /// </summary>
    public async Task AcceptCall(string callerConnectionId, string callId)
    {
        _registry.BeginCall(Context.ConnectionId, callerConnectionId);

        await Clients.Client(callerConnectionId)
            .SendAsync(ClientMethods.CallAccepted, Context.ConnectionId, callId);

        await BroadcastRosterAsync();
    }

    public async Task RejectCall(string callerConnectionId, string callId, string reason)
    {
        await Clients.Client(callerConnectionId)
            .SendAsync(ClientMethods.CallRejected, Context.ConnectionId,
                string.IsNullOrWhiteSpace(reason) ? CallEndReasons.Rejected : reason);
    }

    public async Task EndCall(string peerConnectionId, string callId, string reason)
    {
        _registry.EndCall(Context.ConnectionId);

        if (!string.IsNullOrWhiteSpace(peerConnectionId))
        {
            await Clients.Client(peerConnectionId)
                .SendAsync(ClientMethods.CallEnded, Context.ConnectionId,
                    string.IsNullOrWhiteSpace(reason) ? CallEndReasons.HungUp : reason);
        }

        await BroadcastRosterAsync();
    }

    // -------------------------------------------------------- WebRTC relaying

    /// <summary>
    /// Relays an SDP offer or answer. The sender's claimed FromConnectionId is
    /// overwritten with the real one so a client cannot impersonate a peer.
    /// </summary>
    public Task SendSdp(SdpMessage message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.ToConnectionId))
        {
            return Task.CompletedTask;
        }

        message.FromConnectionId = Context.ConnectionId;

        _log.LogDebug("Relaying {Type} from {From} to {To}",
            message.Type, message.FromConnectionId, message.ToConnectionId);

        return Clients.Client(message.ToConnectionId)
            .SendAsync(ClientMethods.ReceiveSdp, message);
    }

    /// <summary>Relays one trickled ICE candidate. On an intranet these are host candidates.</summary>
    public Task SendIceCandidate(IceCandidateMessage message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.ToConnectionId))
        {
            return Task.CompletedTask;
        }

        message.FromConnectionId = Context.ConnectionId;

        return Clients.Client(message.ToConnectionId)
            .SendAsync(ClientMethods.ReceiveIceCandidate, message);
    }

    // ------------------------------------------------------------------ chat

    public async Task SendChat(string targetConnectionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var me = _registry.Get(Context.ConnectionId);

        var message = new ChatMessage
        {
            FromConnectionId = Context.ConnectionId,
            FromDisplayName = me?.DisplayName ?? "Unknown",
            ToConnectionId = targetConnectionId,
            Text = text,
            SentAtUtc = DateTimeOffset.UtcNow
        };

        // Echo to the sender too, so both windows render from the same object.
        await Clients.Client(targetConnectionId).SendAsync(ClientMethods.ReceiveChat, message);
        await Clients.Caller.SendAsync(ClientMethods.ReceiveChat, message);
    }
}
