using System.Collections.ObjectModel;
using System.Windows;
using IntraTeams.Client.Infrastructure;
using IntraTeams.Client.Media;
using IntraTeams.Client.Rtc;
using IntraTeams.Client.Signaling;
using IntraTeams.Shared;
using SIPSorcery.Net;

namespace IntraTeams.Client.ViewModels;

public enum CallState
{
    Idle = 0,
    Dialing = 1,
    Ringing = 2,
    Connecting = 3,
    InCall = 4
}

/// <summary>
/// Drives the whole window: connection, roster, the call state machine, media
/// toggles and chat.
///
/// The call state machine deliberately keeps offer creation on the CALLER only:
///   caller  InviteCall -> (accepted) -> createOffer -> send SDP -> apply answer
///   callee  IncomingCall -> AcceptCall -> apply offer -> createAnswer -> send
/// Because only one side ever offers, there is no glare and no rollback logic.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly SignalingClient _signaling;

    private MediaStack? _media;
    private PeerSession? _peer;

    private string _currentCallId = "";
    private string _peerConnectionId = "";
    private string _peerDisplayName = "";
    private CallInvite? _pendingInvite;

    private CallState _state = CallState.Idle;
    private string _statusText = "Starting up...";
    private string _connectionBanner = "";
    private bool _isMuted;
    private bool _isCameraOn;
    private bool _isScreenSharing;
    private string _chatDraft = "";
    private UserItemViewModel? _selectedUser;

    public MainViewModel(AppConfig config)
    {
        _config = config;

        MyDisplayName = string.IsNullOrWhiteSpace(config.DisplayNameSuffix)
            ? $"{Environment.UserName} ({Environment.MachineName})"
            : $"{Environment.UserName} ({Environment.MachineName}) [{config.DisplayNameSuffix}]";

        _signaling = new SignalingClient(config.ServerUrl);
        WireSignaling();

        LocalVideo.SourceChanged += () => Raise(nameof(LocalVideoSource));
        RemoteVideo.SourceChanged += () => Raise(nameof(RemoteVideoSource));

        AudioCallCommand = new RelayCommand(_ => StartCall(withVideo: false), _ => CanStartCall());
        VideoCallCommand = new RelayCommand(_ => StartCall(withVideo: true), _ => CanStartCall());
        AcceptCommand = new RelayCommand(_ => AcceptCall(), _ => State == CallState.Ringing);
        RejectCommand = new RelayCommand(_ => RejectCall(), _ => State == CallState.Ringing);
        HangUpCommand = new RelayCommand(_ => HangUp(), _ => State != CallState.Idle);
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute(), _ => State == CallState.InCall);
        ToggleCameraCommand = new RelayCommand(_ => ToggleCamera(), _ => State == CallState.InCall);
        ToggleScreenShareCommand = new RelayCommand(_ => ToggleScreenShare(), _ => State == CallState.InCall);
        SendChatCommand = new RelayCommand(_ => SendChat(), _ => CanSendChat());
    }

    // ------------------------------------------------------------- bound data

    public string MyDisplayName { get; }

    public ObservableCollection<UserItemViewModel> People { get; } = new();

    public ObservableCollection<ChatEntryViewModel> Chat { get; } = new();

    public ObservableCollection<string> Diagnostics { get; } = new();

    public VideoSurface LocalVideo { get; } = new();

    public VideoSurface RemoteVideo { get; } = new();

    public System.Windows.Media.ImageSource? LocalVideoSource => LocalVideo.Source;

    public System.Windows.Media.ImageSource? RemoteVideoSource => RemoteVideo.Source;

    public UserItemViewModel? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (Set(ref _selectedUser, value))
            {
                RefreshCommands();
            }
        }
    }

    public CallState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(IsIdle));
                Raise(nameof(IsRinging));
                Raise(nameof(IsInCall));
                Raise(nameof(IsCallActive));
                RefreshCommands();
            }
        }
    }

    public bool IsIdle => State == CallState.Idle;
    public bool IsRinging => State == CallState.Ringing;
    public bool IsInCall => State == CallState.InCall;
    public bool IsCallActive => State != CallState.Idle;

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string ConnectionBanner
    {
        get => _connectionBanner;
        private set
        {
            if (Set(ref _connectionBanner, value))
            {
                Raise(nameof(HasConnectionBanner));
            }
        }
    }

    public bool HasConnectionBanner => !string.IsNullOrEmpty(ConnectionBanner);

    public string PeerDisplayName
    {
        get => _peerDisplayName;
        private set => Set(ref _peerDisplayName, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (Set(ref _isMuted, value))
            {
                Raise(nameof(MuteButtonText));
            }
        }
    }

    public string MuteButtonText => IsMuted ? "Unmute" : "Mute";

    public bool IsCameraOn
    {
        get => _isCameraOn;
        private set
        {
            if (Set(ref _isCameraOn, value))
            {
                Raise(nameof(CameraButtonText));
            }
        }
    }

    public string CameraButtonText => IsCameraOn ? "Stop video" : "Start video";

    public bool IsScreenSharing
    {
        get => _isScreenSharing;
        private set
        {
            if (Set(ref _isScreenSharing, value))
            {
                Raise(nameof(ScreenShareButtonText));
            }
        }
    }

    public string ScreenShareButtonText => IsScreenSharing ? "Stop sharing" : "Share screen";

    public string ChatDraft
    {
        get => _chatDraft;
        set
        {
            if (Set(ref _chatDraft, value))
            {
                RefreshCommands();
            }
        }
    }

    // ---------------------------------------------------------------- commands

    public RelayCommand AudioCallCommand { get; }
    public RelayCommand VideoCallCommand { get; }
    public RelayCommand AcceptCommand { get; }
    public RelayCommand RejectCommand { get; }
    public RelayCommand HangUpCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand ToggleCameraCommand { get; }
    public RelayCommand ToggleScreenShareCommand { get; }
    public RelayCommand SendChatCommand { get; }

    private void RefreshCommands()
    {
        AudioCallCommand.RaiseCanExecuteChanged();
        VideoCallCommand.RaiseCanExecuteChanged();
        AcceptCommand.RaiseCanExecuteChanged();
        RejectCommand.RaiseCanExecuteChanged();
        HangUpCommand.RaiseCanExecuteChanged();
        ToggleMuteCommand.RaiseCanExecuteChanged();
        ToggleCameraCommand.RaiseCanExecuteChanged();
        ToggleScreenShareCommand.RaiseCanExecuteChanged();
        SendChatCommand.RaiseCanExecuteChanged();
    }

    private bool CanStartCall()
        => State == CallState.Idle && SelectedUser is { IsAvailable: true };

    private bool CanSendChat()
        => !string.IsNullOrWhiteSpace(ChatDraft) &&
           (SelectedUser is not null || State != CallState.Idle);

    // -------------------------------------------------------------- lifecycle

    public async Task StartAsync()
    {
        try
        {
            StatusText = $"Connecting to {_config.ServerUrl}...";
            await _signaling.ConnectAsync();

            var roster = await _signaling.RegisterAsync(Environment.UserName,
                string.IsNullOrWhiteSpace(_config.DisplayNameSuffix)
                    ? Environment.MachineName
                    : $"{Environment.MachineName}/{_config.DisplayNameSuffix}");

            ApplyRoster(roster);
            ConnectionBanner = "";
            StatusText = "Connected. Pick someone to call.";
            Log($"Registered as {MyDisplayName}");
        }
        catch (Exception ex)
        {
            ConnectionBanner = $"Cannot reach the signaling server at {_config.ServerUrl} - {ex.Message}";
            StatusText = "Not connected.";
            Log($"Connect failed: {ex.Message}");
        }
    }

    private void WireSignaling()
    {
        _signaling.RosterChanged += roster => OnUi(() => ApplyRoster(roster));

        _signaling.IncomingCall += invite => OnUi(() => OnIncomingCall(invite));

        _signaling.CallAccepted += (from, callId) => OnUi(() => _ = OnCallAcceptedAsync(from, callId));

        _signaling.CallRejected += (from, reason) => OnUi(() =>
        {
            if (State is CallState.Dialing or CallState.Connecting)
            {
                StatusText = reason == CallEndReasons.Busy
                    ? $"{PeerDisplayName} is already in a call."
                    : $"{PeerDisplayName} declined.";
                _ = TeardownCallAsync();
            }
        });

        _signaling.CallEnded += (from, reason) => OnUi(() =>
        {
            if (State == CallState.Idle)
            {
                return;
            }

            StatusText = reason switch
            {
                CallEndReasons.PeerDisconnected => $"{PeerDisplayName} disconnected.",
                CallEndReasons.Rejected => $"{PeerDisplayName} declined.",
                _ => "Call ended."
            };

            _ = TeardownCallAsync();
        });

        _signaling.SdpReceived += message => OnUi(() => _ = OnSdpReceivedAsync(message));

        _signaling.IceCandidateReceived += message => OnUi(() =>
        {
            _peer?.AddRemoteIceCandidate(
                message.Candidate, message.SdpMid, message.SdpMLineIndex, message.UsernameFragment);
        });

        _signaling.ChatReceived += message => OnUi(() =>
        {
            var isMine = message.FromConnectionId == _signaling.ConnectionId;
            Chat.Add(new ChatEntryViewModel(
                message.FromDisplayName, message.Text, message.SentAtUtc, isMine));
        });

        _signaling.Reconnecting += () => OnUi(() =>
            ConnectionBanner = "Connection lost. Reconnecting...");

        _signaling.Reconnected += () => OnUi(() =>
        {
            ConnectionBanner = "";
            // A new connection id means a new identity to everyone else.
            _ = ReregisterAsync();
        });

        _signaling.Closed += error => OnUi(() =>
            ConnectionBanner = $"Disconnected from the server. {error}");
    }

    private async Task ReregisterAsync()
    {
        try
        {
            var roster = await _signaling.RegisterAsync(Environment.UserName, Environment.MachineName);
            ApplyRoster(roster);
        }
        catch (Exception ex)
        {
            Log($"Re-register failed: {ex.Message}");
        }
    }

    private void ApplyRoster(IReadOnlyList<UserInfo> roster)
    {
        var mine = _signaling.ConnectionId;
        var incoming = roster.Where(u => u.ConnectionId != mine).ToList();

        // Update in place so selection and scroll position survive a refresh.
        foreach (var user in incoming)
        {
            var existing = People.FirstOrDefault(p => p.ConnectionId == user.ConnectionId);
            if (existing is null)
            {
                People.Add(new UserItemViewModel(user));
            }
            else
            {
                existing.Update(user);
            }
        }

        var stale = People.Where(p => incoming.All(u => u.ConnectionId != p.ConnectionId)).ToList();
        foreach (var gone in stale)
        {
            People.Remove(gone);
        }

        RefreshCommands();
    }

    // ------------------------------------------------------------ call: start

    private void StartCall(bool withVideo)
    {
        if (SelectedUser is null)
        {
            return;
        }

        _peerConnectionId = SelectedUser.ConnectionId;
        PeerDisplayName = SelectedUser.DisplayName;
        _currentCallId = Guid.NewGuid().ToString("N");

        State = CallState.Dialing;
        StatusText = $"Calling {PeerDisplayName}...";
        Log($"Inviting {PeerDisplayName} (video={withVideo})");

        _pendingStartWithVideo = withVideo;

        _ = SafeAsync(() => _signaling.InviteAsync(_peerConnectionId, _currentCallId, withVideo),
            "Could not place the call");
    }

    private bool _pendingStartWithVideo;

    /// <summary>Caller side: the callee picked up, so build the session and offer.</summary>
    private async Task OnCallAcceptedAsync(string fromConnectionId, string callId)
    {
        if (State != CallState.Dialing || fromConnectionId != _peerConnectionId)
        {
            return;
        }

        State = CallState.Connecting;
        StatusText = $"Connecting to {PeerDisplayName}...";

        try
        {
            await BuildSessionAsync(_pendingStartWithVideo);

            var offer = await _peer!.CreateOfferAsync();

            await _signaling.SendSdpAsync(new SdpMessage
            {
                CallId = _currentCallId,
                ToConnectionId = _peerConnectionId,
                Type = "offer",
                Sdp = offer.sdp
            });

            Log("Offer sent.");
        }
        catch (Exception ex)
        {
            Log($"Failed to create the offer: {ex.Message}");
            StatusText = "Could not start the call.";
            await TeardownCallAsync();
        }
    }

    // ----------------------------------------------------------- call: answer

    private void OnIncomingCall(CallInvite invite)
    {
        if (State != CallState.Idle)
        {
            // Already busy: decline immediately so the caller is not left ringing.
            _ = SafeAsync(
                () => _signaling.RejectAsync(invite.FromConnectionId, invite.CallId, CallEndReasons.Busy),
                "Could not decline the call");
            return;
        }

        _pendingInvite = invite;
        _peerConnectionId = invite.FromConnectionId;
        PeerDisplayName = invite.FromDisplayName;
        _currentCallId = invite.CallId;

        State = CallState.Ringing;
        StatusText = invite.WithVideo
            ? $"{invite.FromDisplayName} is calling with video"
            : $"{invite.FromDisplayName} is calling";

        Log($"Incoming call from {invite.FromDisplayName}");
    }

    private void AcceptCall()
    {
        if (_pendingInvite is null)
        {
            return;
        }

        var invite = _pendingInvite;
        _pendingInvite = null;

        State = CallState.Connecting;
        StatusText = $"Connecting to {PeerDisplayName}...";

        _ = SafeAsync(async () =>
        {
            // Devices come up BEFORE accepting, so the offer that follows
            // immediately afterwards is answered by a session that is ready.
            await BuildSessionAsync(invite.WithVideo);
            await _signaling.AcceptAsync(invite.FromConnectionId, invite.CallId);
            Log("Call accepted.");
        }, "Could not accept the call");
    }

    private void RejectCall()
    {
        if (_pendingInvite is null)
        {
            return;
        }

        var invite = _pendingInvite;
        _pendingInvite = null;

        _ = SafeAsync(
            () => _signaling.RejectAsync(invite.FromConnectionId, invite.CallId, CallEndReasons.Rejected),
            "Could not decline the call");

        StatusText = "Call declined.";
        _ = TeardownCallAsync();
    }

    private async Task OnSdpReceivedAsync(SdpMessage message)
    {
        if (_peer is null)
        {
            Log("SDP arrived before the session existed - ignoring.");
            return;
        }

        if (message.FromConnectionId != _peerConnectionId)
        {
            return;
        }

        var isOffer = string.Equals(message.Type, "offer", StringComparison.OrdinalIgnoreCase);

        if (!_peer.SetRemoteDescription(message.Type, message.Sdp))
        {
            StatusText = "Media negotiation failed.";
            await TeardownCallAsync();
            return;
        }

        Log($"Applied remote {message.Type}.");

        if (!isOffer)
        {
            return;
        }

        try
        {
            var answer = await _peer.CreateAnswerAsync();

            await _signaling.SendSdpAsync(new SdpMessage
            {
                CallId = _currentCallId,
                ToConnectionId = _peerConnectionId,
                Type = "answer",
                Sdp = answer.sdp
            });

            Log("Answer sent.");
        }
        catch (Exception ex)
        {
            Log($"Failed to answer: {ex.Message}");
            await TeardownCallAsync();
        }
    }

    // ---------------------------------------------------------- session setup

    private async Task BuildSessionAsync(bool withVideo)
    {
        _media = new MediaStack(_config);
        _media.Error += message => OnUi(() => Log(message));
        _media.Info += message => OnUi(() => Log(message));

        // The media stack owns which producer is live; mirror it rather than
        // tracking the same state twice and letting the two drift.
        _media.VideoSourceChanged += source => OnUi(() =>
        {
            IsCameraOn = source == VideoSourceKind.Camera;
            IsScreenSharing = source == VideoSourceKind.Screen;

            if (source == VideoSourceKind.None)
            {
                LocalVideo.Clear();
            }
        });
        _media.LocalFrameReady += (frame, width, height, format)
            => LocalVideo.Update(frame, width, height, width * BytesPerPixel(format), format);
        _media.RemoteFrameReady += (frame, width, height, stride, format)
            => RemoteVideo.Update(frame, width, height, stride, format);

        // NOTE: the microphone and speaker are deliberately NOT started here.
        // PeerSession opens them on the transition to "connected", once the
        // audio codec has been negotiated and applied. Starting them at this
        // point instead gives a call that connects and stays silent.

        _peer = new PeerSession(_media);
        _peer.Diagnostic += message => OnUi(() => Log(message));
        _peer.LocalIceCandidate += OnLocalIceCandidate;
        _peer.ConnectionStateChanged += state => OnUi(() => OnPeerStateChanged(state));

        // Only now is it safe to let candidates out. Host-only gathering has no
        // network round trip, so some may already have been raised while the
        // constructor above was still running; this releases them.
        _peer.StartLocalCandidateFlow();

        IsMuted = false;
        IsScreenSharing = false;
        IsCameraOn = false;

        // Deferred on purpose. PeerSession starts the camera on the transition
        // to "connected", after the video format has been negotiated and pushed
        // into the camera endpoint.
        _media.CameraRequested = withVideo;

        await Task.CompletedTask;
    }

    private static int BytesPerPixel(SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum format)
        => format is SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.Bgra
            or SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.Rgba
            ? 4
            : 3;

    private void OnLocalIceCandidate(RTCIceCandidate candidate)
    {
        var message = new IceCandidateMessage
        {
            CallId = _currentCallId,
            ToConnectionId = _peerConnectionId,
            Candidate = candidate.candidate,
            SdpMid = candidate.sdpMid,
            SdpMLineIndex = candidate.sdpMLineIndex,
            UsernameFragment = candidate.usernameFragment
        };

        _ = SafeAsync(() => _signaling.SendIceCandidateAsync(message), "Could not send an ICE candidate");
    }

    private void OnPeerStateChanged(RTCPeerConnectionState state)
    {
        switch (state)
        {
            case RTCPeerConnectionState.connected:
                State = CallState.InCall;
                StatusText = $"In a call with {PeerDisplayName}";
                break;

            case RTCPeerConnectionState.failed:
                StatusText = "Media connection failed. Check that both machines can reach each other directly.";
                _ = TeardownCallAsync();
                break;

            case RTCPeerConnectionState.disconnected:
                StatusText = "Media connection lost.";
                break;

            case RTCPeerConnectionState.closed:
                if (State != CallState.Idle)
                {
                    _ = TeardownCallAsync();
                }

                break;
        }
    }

    // -------------------------------------------------------- in-call toggles

    private void ToggleMute()
    {
        if (_media is null)
        {
            return;
        }

        IsMuted = !IsMuted;
        _media.IsMicrophoneMuted = IsMuted;
        Log(IsMuted ? "Microphone muted." : "Microphone live.");
    }

    private void ToggleCamera()
    {
        if (_media is null)
        {
            return;
        }

        var media = _media;

        // IsCameraOn / IsScreenSharing are updated by the VideoSourceChanged
        // subscription, so this only asks for the change.
        _ = SafeAsync(async () =>
        {
            if (IsCameraOn)
            {
                media.CameraRequested = false;
                await media.StopCameraAsync();
            }
            else
            {
                media.CameraRequested = true;
                await media.StartCameraAsync();
            }
        }, "Camera toggle failed");
    }

    private void ToggleScreenShare()
    {
        if (_media is null)
        {
            return;
        }

        if (IsScreenSharing)
        {
            _media.StopScreenShare();
            Log("Screen sharing stopped.");
        }
        else
        {
            // Screen and camera are mutually exclusive: one track, one producer.
            _media.CameraRequested = false;
            _media.StartScreenShare();
            Log("Screen sharing started.");
        }
    }

    // ------------------------------------------------------------------ chat

    private void SendChat()
    {
        var target = State != CallState.Idle ? _peerConnectionId : SelectedUser?.ConnectionId;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var text = ChatDraft.Trim();
        ChatDraft = "";

        _ = SafeAsync(() => _signaling.SendChatAsync(target, text), "Could not send the message");
    }

    // -------------------------------------------------------------- teardown

    private void HangUp()
    {
        StatusText = "Call ended.";

        if (!string.IsNullOrEmpty(_peerConnectionId))
        {
            var reason = State == CallState.Dialing ? CallEndReasons.Cancelled : CallEndReasons.HungUp;
            _ = SafeAsync(() => _signaling.EndCallAsync(_peerConnectionId, _currentCallId, reason),
                "Could not signal hang up");
        }

        _ = TeardownCallAsync();
    }

    private async Task TeardownCallAsync()
    {
        var peer = _peer;
        var media = _media;

        _peer = null;
        _media = null;
        _pendingInvite = null;
        _peerConnectionId = "";
        _currentCallId = "";

        State = CallState.Idle;
        IsMuted = false;
        IsCameraOn = false;
        IsScreenSharing = false;

        LocalVideo.Clear();
        RemoteVideo.Clear();

        if (peer is not null)
        {
            await peer.DisposeAsync();
        }

        if (media is not null)
        {
            await media.DisposeAsync();
        }

        Log("Session torn down.");
    }

    // -------------------------------------------------------------- utilities

    private void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";

        OnUi(() =>
        {
            Diagnostics.Add(line);

            // Keep the diagnostics pane bounded; it is a debugging aid, not a log file.
            while (Diagnostics.Count > 300)
            {
                Diagnostics.RemoveAt(0);
            }
        });
    }

    /// <summary>Marshals to the UI thread. SignalR and media callbacks arrive on pool threads.</summary>
    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>Runs work that must never crash the app on a background fault.</summary>
    private Task SafeAsync(Func<Task> work, string context)
    {
        return Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                Log($"{context}: {ex.Message}");
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await TeardownCallAsync();
        await _signaling.DisposeAsync();
    }
}
