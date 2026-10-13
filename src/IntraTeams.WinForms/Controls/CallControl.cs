using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using IntraTeams.Client.Infrastructure;
using IntraTeams.Client.Media;
using IntraTeams.Client.Rtc;
using RTCPeerConnectionState = SIPSorcery.Net.RTCPeerConnectionState;
using VideoPixelFormatsEnum = SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum;

namespace IntraTeams.WinForms.Controls;

/// <summary>
/// Self-contained audio / video / screen-share surface for ONE 1-to-1 call,
/// built on SIPSorcery (via the shared PeerSession and MediaStack).
///
/// The control deliberately knows NOTHING about signaling, SignalR, users or
/// chat. It only speaks WebRTC primitives, so any host - the IntraTeams
/// wrapper form, or some other legacy form with its own transport - can drop
/// it in. The contract with the host is:
///
///   Host -> control (call on the UI thread)
///     StartOutgoingCallAsync   caller: build the session and produce an offer
///     PrepareIncomingCall      callee: build the session, wait for the offer
///     ApplyRemoteDescriptionAsync   deliver the peer's offer/answer
///     AddRemoteIceCandidate    deliver one of the peer's ICE candidates
///     EndCallAsync             tear everything down
///
///   Control -> host (always raised on the UI thread)
///     LocalSdpReady            send this offer/answer to the peer
///     LocalIceCandidateReady   send this candidate to the peer
///     StateChanged             Connecting / Connected / Failed / Idle
///     HangUpRequested          the user pressed Hang up
///     Diagnostic               log line for the host's connection log
///
/// The control never ends a call on its own. On failure it reports
/// <see cref="CallControlState.Failed"/>; on Hang up it raises
/// <see cref="HangUpRequested"/>. The host decides what to signal to the peer
/// and then calls <see cref="EndCallAsync"/>. That keeps call lifecycle in one
/// place - the host - which is what makes the control reusable.
///
/// ICE policy is inherited from PeerSession: no STUN, no TURN, host
/// candidates only.
/// </summary>
[DefaultEvent(nameof(HangUpRequested))]
[Description("1-to-1 WebRTC audio, video and screen sharing (SIPSorcery). Signaling is supplied by the host.")]
public partial class CallControl : UserControl
{
    private AppConfig _settings = new();

    private MediaStack? _media;
    private PeerSession? _peer;

    private CallControlState _state = CallControlState.Idle;
    private string _peerDisplayName = "";
    private bool _cameraToggleBusy;

    public CallControl()
    {
        InitializeComponent();
        ResetButtons();
    }

    // ------------------------------------------------------------------ events

    /// <summary>An offer or answer is ready; the host must deliver it to the peer.</summary>
    [Category("Signaling")]
    public event EventHandler<LocalSdpEventArgs>? LocalSdpReady;

    /// <summary>A local ICE candidate is ready; the host must deliver it to the peer.</summary>
    [Category("Signaling")]
    public event EventHandler<LocalIceCandidateEventArgs>? LocalIceCandidateReady;

    [Category("Call")]
    public event EventHandler<CallStateChangedEventArgs>? StateChanged;

    /// <summary>The user pressed Hang up. The host should signal the peer, then call <see cref="EndCallAsync"/>.</summary>
    [Category("Call")]
    public event EventHandler? HangUpRequested;

    [Category("Call")]
    public event EventHandler<DiagnosticEventArgs>? Diagnostic;

    // -------------------------------------------------------------- properties

    /// <summary>Camera and screen-share settings. Set by the host before the first call.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppConfig Settings
    {
        get => _settings;
        set => _settings = value ?? new AppConfig();
    }

    [Browsable(false)]
    public CallControlState State => _state;

    /// <summary>True from Start/Prepare until EndCallAsync.</summary>
    [Browsable(false)]
    public bool HasSession => _peer is not null;

    [Browsable(false)]
    public bool IsMuted => _media?.IsMicrophoneMuted ?? false;

    [Browsable(false)]
    public bool IsCameraOn => _media?.ActiveVideoSource == VideoSourceKind.Camera;

    [Browsable(false)]
    public bool IsScreenSharing => _media?.ActiveVideoSource == VideoSourceKind.Screen;

    // ------------------------------------------------------- host -> control

    /// <summary>
    /// Caller side. Builds the session and raises <see cref="LocalSdpReady"/>
    /// with the offer. Call once the callee has accepted.
    /// </summary>
    public async Task StartOutgoingCallAsync(string peerDisplayName, bool withVideo)
    {
        EnsureUiThread();
        BuildSession(peerDisplayName, withVideo);

        var peer = _peer!;

        try
        {
            var offer = await peer.CreateOfferAsync();

            if (!ReferenceEquals(peer, _peer))
            {
                return; // Ended while the offer was being built.
            }

            Log("Offer created.");
            LocalSdpReady?.Invoke(this, new LocalSdpEventArgs("offer", offer.sdp ?? string.Empty));
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(peer, _peer))
            {
                Fail($"Could not create the offer: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Callee side. Builds the session so it is ready for the offer that will
    /// follow. Call this BEFORE telling the caller you accepted.
    /// </summary>
    public void PrepareIncomingCall(string peerDisplayName, bool withVideo)
    {
        EnsureUiThread();
        BuildSession(peerDisplayName, withVideo);
    }

    /// <summary>
    /// Applies the peer's offer or answer. For an offer, an answer is produced
    /// and raised through <see cref="LocalSdpReady"/>.
    /// </summary>
    public async Task ApplyRemoteDescriptionAsync(string type, string sdp)
    {
        EnsureUiThread();

        var peer = _peer;
        if (peer is null)
        {
            Log($"Remote {type} arrived with no active session - ignored.");
            return;
        }

        if (!peer.SetRemoteDescription(type, sdp))
        {
            Fail("Media negotiation failed: the remote description was rejected.");
            return;
        }

        Log($"Applied remote {type}.");

        if (!string.Equals(type, "offer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var answer = await peer.CreateAnswerAsync();

            if (!ReferenceEquals(peer, _peer))
            {
                return;
            }

            Log("Answer created.");
            LocalSdpReady?.Invoke(this, new LocalSdpEventArgs("answer", answer.sdp ?? string.Empty));
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(peer, _peer))
            {
                Fail($"Could not create the answer: {ex.Message}");
            }
        }
    }

    /// <summary>Adds a candidate from the peer. Queued internally if it arrives before the remote description.</summary>
    public void AddRemoteIceCandidate(string candidate, string? sdpMid, int sdpMLineIndex, string? usernameFragment)
    {
        EnsureUiThread();
        _peer?.AddRemoteIceCandidate(candidate, sdpMid, sdpMLineIndex, usernameFragment);
    }

    /// <summary>Closes the peer connection and every device. Safe to call when idle.</summary>
    public async Task EndCallAsync(string? statusText = null)
    {
        EnsureUiThread();

        var peer = _peer;
        var media = _media;

        _peer = null;
        _media = null;
        _peerDisplayName = "";

        ResetButtons();
        localVideo.Clear();
        localVideo.Visible = false;
        remoteVideo.Clear();

        SetState(CallControlState.Idle, statusText ?? "No active call.");

        if (peer is not null)
        {
            await peer.DisposeAsync();
        }

        if (media is not null)
        {
            await media.DisposeAsync();
        }

        if (peer is not null || media is not null)
        {
            Log("Media session torn down.");
        }
    }

    // ------------------------------------------------------------ session setup

    private void BuildSession(string peerDisplayName, bool withVideo)
    {
        if (_peer is not null)
        {
            throw new InvalidOperationException("This CallControl already has an active call. Call EndCallAsync first.");
        }

        _peerDisplayName = peerDisplayName;

        var media = new MediaStack(_settings);
        media.Error += message => Ui(() => Log(message));
        media.Info += message => Ui(() => Log(message));
        media.VideoSourceChanged += source => Ui(() => OnVideoSourceChanged(media, source));

        // Frames go straight to the views, which marshal and drop as needed.
        // The identity check stops a late frame from a torn-down session
        // repainting a view that has just been cleared.
        media.LocalFrameReady += (frame, width, height, format) =>
        {
            if (ReferenceEquals(media, _media))
            {
                localVideo.PushFrame(frame, width, height, width * BytesPerPixel(format), format);
            }
        };

        media.RemoteFrameReady += (frame, width, height, stride, format) =>
        {
            if (ReferenceEquals(media, _media))
            {
                remoteVideo.PushFrame(frame, width, height, stride, format);
            }
        };

        // NOTE: the microphone, speaker and camera are NOT opened here.
        // PeerSession opens them on the transition to "connected", after the
        // codecs have been negotiated - see the ORDERING RULE in MediaStack.
        media.CameraRequested = withVideo;

        PeerSession peer;

        try
        {
            peer = new PeerSession(media);
        }
        catch
        {
            // Do not leak the audio endpoint MediaStack may already have opened.
            _ = media.DisposeAsync().AsTask();
            throw;
        }

        peer.Diagnostic += message => Ui(() => Log(message));
        peer.ConnectionStateChanged += state => Ui(() => OnPeerStateChanged(peer, state));
        peer.LocalIceCandidate += candidate => Ui(() =>
        {
            if (ReferenceEquals(peer, _peer))
            {
                LocalIceCandidateReady?.Invoke(this, new LocalIceCandidateEventArgs(
                    candidate.candidate, candidate.sdpMid, candidate.sdpMLineIndex, candidate.usernameFragment));
            }
        });

        _media = media;
        _peer = peer;

        // Only now is anyone listening, so release candidates gathered while
        // PeerSession's constructor was still running.
        peer.StartLocalCandidateFlow();

        btnHangUp.Enabled = true;
        SetState(CallControlState.Connecting, $"Connecting to {peerDisplayName}...");
    }

    private void OnPeerStateChanged(PeerSession peer, RTCPeerConnectionState state)
    {
        if (!ReferenceEquals(peer, _peer))
        {
            return; // A late event from a session that has already been torn down.
        }

        switch (state)
        {
            case RTCPeerConnectionState.connected:
                btnMute.Enabled = true;
                btnCamera.Enabled = true;
                btnShare.Enabled = true;
                SetState(CallControlState.Connected, $"In a call with {_peerDisplayName}");
                break;

            case RTCPeerConnectionState.failed:
                Fail("Media connection failed. Check that both machines can reach each other " +
                     "directly (firewall prompt, VPN, Wi-Fi client isolation).");
                break;

            case RTCPeerConnectionState.disconnected:
                lblStatus.Text = "Media connection lost - waiting for it to recover...";
                break;
        }
    }

    private void OnVideoSourceChanged(MediaStack media, VideoSourceKind source)
    {
        if (!ReferenceEquals(media, _media))
        {
            return;
        }

        btnCamera.Text = source == VideoSourceKind.Camera ? "Stop video" : "Start video";
        btnShare.Text = source == VideoSourceKind.Screen ? "Stop sharing" : "Share screen";

        // The preview box only shows while we are sending something.
        localVideo.Visible = source != VideoSourceKind.None;
        localVideo.PlaceholderText = source == VideoSourceKind.Screen ? "Your screen" : "You";

        if (source == VideoSourceKind.None)
        {
            localVideo.Clear();
        }
    }

    // ----------------------------------------------------------------- buttons

    private void btnMute_Click(object? sender, EventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        _media.IsMicrophoneMuted = !_media.IsMicrophoneMuted;
        btnMute.Text = _media.IsMicrophoneMuted ? "Unmute" : "Mute";
        Log(_media.IsMicrophoneMuted ? "Microphone muted." : "Microphone live.");
    }

    private async void btnCamera_Click(object? sender, EventArgs e)
    {
        var media = _media;
        if (media is null || _cameraToggleBusy)
        {
            return;
        }

        _cameraToggleBusy = true;
        btnCamera.Enabled = false;

        try
        {
            // Button text follows VideoSourceChanged, so this only requests the change.
            if (media.ActiveVideoSource == VideoSourceKind.Camera)
            {
                media.CameraRequested = false;
                await media.StopCameraAsync();
            }
            else
            {
                media.CameraRequested = true;
                await media.StartCameraAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"Camera toggle failed: {ex.Message}");
        }
        finally
        {
            _cameraToggleBusy = false;
            btnCamera.Enabled = ReferenceEquals(media, _media) && _state == CallControlState.Connected;
        }
    }

    private void btnShare_Click(object? sender, EventArgs e)
    {
        if (_media is null)
        {
            return;
        }

        if (_media.ActiveVideoSource == VideoSourceKind.Screen)
        {
            _media.StopScreenShare();
            Log("Screen sharing stopped.");
        }
        else
        {
            // One video track, one producer: sharing replaces the camera.
            _media.CameraRequested = false;
            _media.StartScreenShare();
            Log("Screen sharing started.");
        }
    }

    private void btnHangUp_Click(object? sender, EventArgs e)
        => HangUpRequested?.Invoke(this, EventArgs.Empty);

    // ----------------------------------------------------------------- helpers

    private void ResetButtons()
    {
        btnMute.Text = "Mute";
        btnCamera.Text = "Start video";
        btnShare.Text = "Share screen";

        btnMute.Enabled = false;
        btnCamera.Enabled = false;
        btnShare.Enabled = false;
        btnHangUp.Enabled = false;
    }

    private void Fail(string message)
    {
        Log(message);
        SetState(CallControlState.Failed, message);
    }

    private void SetState(CallControlState state, string detail)
    {
        _state = state;
        lblStatus.Text = detail;
        StateChanged?.Invoke(this, new CallStateChangedEventArgs(state, detail));
    }

    private void Log(string message) => Diagnostic?.Invoke(this, new DiagnosticEventArgs(message));

    private static int BytesPerPixel(VideoPixelFormatsEnum format)
        => format is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba ? 4 : 3;

    /// <summary>Marshals to the UI thread. SIPSorcery raises events on its own threads.</summary>
    private void Ui(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (!InvokeRequired)
        {
            action();
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException) { }
    }

    private void EnsureUiThread()
    {
        if (InvokeRequired)
        {
            throw new InvalidOperationException("CallControl methods must be called on the UI thread.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Best effort. The host should already have called EndCallAsync;
            // this only stops devices if it did not.
            var peer = _peer;
            var media = _media;
            _peer = null;
            _media = null;

            // Task.Run: no WinForms SynchronizationContext on the pool, so the
            // awaits inside DisposeAsync cannot deadlock against this blocked
            // UI thread. The timeout stops a stuck device from hanging exit.
            if (peer is not null || media is not null)
            {
                try
                {
                    Task.Run(async () =>
                    {
                        if (peer is not null) await peer.DisposeAsync();
                        if (media is not null) await media.DisposeAsync();
                    }).Wait(TimeSpan.FromSeconds(3));
                }
                catch
                {
                    // Shutting down anyway.
                }
            }

            components?.Dispose();
        }

        base.Dispose(disposing);
    }
}
