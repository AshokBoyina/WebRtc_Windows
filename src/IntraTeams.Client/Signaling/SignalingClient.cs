using IntraTeams.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace IntraTeams.Client.Signaling;

/// <summary>
/// Thin wrapper over the SignalR hub connection. Exposes the signaling
/// protocol as ordinary .NET events so the view model never touches SignalR
/// types directly.
/// </summary>
public sealed class SignalingClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public SignalingClient(string serverUrl)
    {
        var hubUrl = serverUrl.TrimEnd('/') + HubRoutes.Signaling;

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            // An intranet POC on office wifi will drop connections; rejoining
            // automatically is much less annoying than restarting the app.
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)
            })
            .Build();

        RegisterHandlers();

        _connection.Reconnecting += _ => Raise(Reconnecting);
        _connection.Reconnected += _ => Raise(Reconnected);
        _connection.Closed += error =>
        {
            Closed?.Invoke(error?.Message);
            return Task.CompletedTask;
        };
    }

    public string ConnectionId => _connection.ConnectionId ?? string.Empty;

    public HubConnectionState State => _connection.State;

    // ----------------------------------------------------------------- events

    public event Action<IReadOnlyList<UserInfo>>? RosterChanged;
    public event Action<CallInvite>? IncomingCall;
    public event Action<string, string>? CallAccepted;      // (fromConnectionId, callId)
    public event Action<string, string>? CallRejected;      // (fromConnectionId, reason)
    public event Action<string, string>? CallEnded;         // (fromConnectionId, reason)
    public event Action<SdpMessage>? SdpReceived;
    public event Action<IceCandidateMessage>? IceCandidateReceived;
    public event Action<ChatMessage>? ChatReceived;

    public event Action? Reconnecting;
    public event Action? Reconnected;
    public event Action<string?>? Closed;

    private static Task Raise(Action? handler)
    {
        handler?.Invoke();
        return Task.CompletedTask;
    }

    private void RegisterHandlers()
    {
        _connection.On<List<UserInfo>>(ClientMethods.RosterChanged,
            roster => RosterChanged?.Invoke(roster));

        _connection.On<CallInvite>(ClientMethods.IncomingCall,
            invite => IncomingCall?.Invoke(invite));

        _connection.On<string, string>(ClientMethods.CallAccepted,
            (from, callId) => CallAccepted?.Invoke(from, callId));

        _connection.On<string, string>(ClientMethods.CallRejected,
            (from, reason) => CallRejected?.Invoke(from, reason));

        _connection.On<string, string>(ClientMethods.CallEnded,
            (from, reason) => CallEnded?.Invoke(from, reason));

        _connection.On<SdpMessage>(ClientMethods.ReceiveSdp,
            message => SdpReceived?.Invoke(message));

        _connection.On<IceCandidateMessage>(ClientMethods.ReceiveIceCandidate,
            message => IceCandidateReceived?.Invoke(message));

        _connection.On<ChatMessage>(ClientMethods.ReceiveChat,
            message => ChatReceived?.Invoke(message));
    }

    // ------------------------------------------------------------- operations

    public Task ConnectAsync(CancellationToken cancellationToken = default)
        => _connection.StartAsync(cancellationToken);

    public Task<List<UserInfo>> RegisterAsync(string userName, string machineName)
        => _connection.InvokeAsync<List<UserInfo>>(HubMethods.Register, userName, machineName);

    public Task InviteAsync(string targetConnectionId, string callId, bool withVideo)
        => _connection.SendAsync(HubMethods.InviteCall, targetConnectionId, callId, withVideo);

    public Task AcceptAsync(string callerConnectionId, string callId)
        => _connection.SendAsync(HubMethods.AcceptCall, callerConnectionId, callId);

    public Task RejectAsync(string callerConnectionId, string callId, string reason)
        => _connection.SendAsync(HubMethods.RejectCall, callerConnectionId, callId, reason);

    public Task EndCallAsync(string peerConnectionId, string callId, string reason)
        => _connection.SendAsync(HubMethods.EndCall, peerConnectionId, callId, reason);

    public Task SendSdpAsync(SdpMessage message)
        => _connection.SendAsync(HubMethods.SendSdp, message);

    public Task SendIceCandidateAsync(IceCandidateMessage message)
        => _connection.SendAsync(HubMethods.SendIceCandidate, message);

    public Task SendChatAsync(string targetConnectionId, string text)
        => _connection.SendAsync(HubMethods.SendChat, targetConnectionId, text);

    public async ValueTask DisposeAsync()
    {
        try { await _connection.StopAsync(); } catch { /* shutting down anyway */ }
        await _connection.DisposeAsync();
    }
}
