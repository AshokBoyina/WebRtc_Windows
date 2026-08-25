using System.Net;
using IntraTeams.Client.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace IntraTeams.Client.Rtc;

/// <summary>
/// One 1-1 WebRTC session: the peer connection, its tracks, and the wiring
/// between the media stack and RTP.
///
/// ICE configuration is the important part for this project. There is no STUN
/// server and no TURN server. On an intranet both peers can reach each other
/// directly, so host candidates alone are sufficient and connectivity checks
/// succeed on the first pair tried. This is the same path Microsoft Teams
/// selects when two clients sit on the same corporate network - it only falls
/// back to its Transport Relay when a direct path fails.
/// </summary>
public sealed class PeerSession : IAsyncDisposable
{
    private readonly MediaStack _media;
    private readonly RTCPeerConnection _pc;

    /// <summary>
    /// Remote candidates that arrived before the remote description was set.
    /// Trickle ICE makes this ordering perfectly normal, and adding a candidate
    /// too early is silently useless, so they are queued instead.
    /// </summary>
    private readonly List<RTCIceCandidateInit> _pendingRemoteCandidates = new();

    /// <summary>
    /// Local candidates gathered before the owner subscribed to
    /// <see cref="LocalIceCandidate"/>.
    ///
    /// This matters specifically because there is no STUN server. Gathering a
    /// server-reflexive candidate takes a network round trip, which leaves
    /// plenty of time to attach an event handler. Host candidates are
    /// enumerated from the local NIC list with no I/O at all, so they can be
    /// raised before the constructor has even returned to the caller. Without
    /// this buffer those candidates are raised into an event with no
    /// subscribers, never reach the peer, and ICE stalls at "checking" forever.
    /// </summary>
    private readonly List<RTCIceCandidate> _pendingLocalCandidates = new();

    private bool _localCandidateFlowStarted;
    private int _localCandidateCount;
    private int _remoteCandidateCount;

    private bool _remoteDescriptionSet;
    private bool _closed;

    public PeerSession(MediaStack media)
    {
        _media = media;

        var configuration = new RTCConfiguration
        {
            // Empty, NOT null: no STUN and no TURN will be contacted, so only
            // host candidates are gathered. This is the whole point of the
            // "no ICE server" requirement.
            iceServers = new List<RTCIceServer>(),

            // Advertise a candidate for every local NIC. On machines with VPN,
            // Hyper-V or Docker adapters the "best" single address is often the
            // wrong one, and a wrong single candidate means no call at all.
            X_ICEIncludeAllInterfaceAddresses = true,

            // With no STUN to wait on, gathering finishes almost immediately.
            // The 30s default just delays failure diagnosis.
            X_GatherTimeoutMs = 3000
        };

        // Enforcement, not just intent. If this ever fires, something has added
        // an ICE server and the "intranet only, no external dependency"
        // guarantee is broken.
        if (configuration.iceServers is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "IntraTeams is built to run with no STUN or TURN servers. " +
                $"Found {configuration.iceServers.Count} configured ICE server(s).");
        }

        _pc = new RTCPeerConnection(configuration);

        AddTracks();
        WireMedia();
        WireDiagnostics();
    }

    // ---------------------------------------------------------------- events

    /// <summary>Raised for each locally gathered ICE candidate, for trickling to the peer.</summary>
    public event Action<RTCIceCandidate>? LocalIceCandidate;

    public event Action<RTCPeerConnectionState>? ConnectionStateChanged;

    public event Action<string>? Diagnostic;

    public RTCPeerConnectionState ConnectionState => _pc.connectionState;

    // ------------------------------------------------------------------ setup

    private void AddTracks()
    {
        var audioTrack = new MediaStreamTrack(
            _media.GetAudioFormats(), MediaStreamStatusEnum.SendRecv);
        _pc.addTrack(audioTrack);

        // The video track is declared from a static VP8 format list rather than
        // from a device, so a machine with no webcam still negotiates video and
        // can therefore still share its screen.
        var videoTrack = new MediaStreamTrack(
            MediaStack.GetVideoFormats(), MediaStreamStatusEnum.SendRecv);
        _pc.addTrack(videoTrack);
    }

    private void WireMedia()
    {
        // Outbound: encoded samples from the media stack into RTP.
        _media.EncodedAudioReady += OnEncodedAudio;
        _media.EncodedVideoReady += OnEncodedVideo;

        // Inbound: RTP into the media stack for playback / decode.
        _pc.OnAudioFrameReceived += OnAudioFrameReceived;
        _pc.OnVideoFrameReceived += OnVideoFrameReceived;

        // Codec negotiation results.
        _pc.OnAudioFormatsNegotiated += formats =>
        {
            if (formats.Count > 0)
            {
                _media.SetNegotiatedAudioFormat(formats[0]);
                Diagnostic?.Invoke($"Audio negotiated: {formats[0].Codec}");
            }
        };

        _pc.OnVideoFormatsNegotiated += formats =>
        {
            if (formats.Count > 0)
            {
                _media.SetNegotiatedVideoFormat(formats[0]);
                Diagnostic?.Invoke($"Video negotiated: {formats[0].Codec}");
            }
        };
    }

    private void WireDiagnostics()
    {
        _pc.onicecandidate += candidate =>
        {
            if (candidate is null)
            {
                return;
            }

            _localCandidateCount++;

            var kind = GetCandidateType(candidate.candidate);
            Diagnostic?.Invoke($"Local candidate #{_localCandidateCount} [{kind}]: {candidate.candidate}");

            // With no ICE servers configured this can only ever be "host".
            // Anything else would mean a server crept into the configuration.
            if (!IsHostCandidate(kind))
            {
                Diagnostic?.Invoke(
                    $"WARNING: local candidate type '{kind}' is not 'host'. " +
                    "That implies a STUN or TURN server is in use. Not sending it.");
                return;
            }

            lock (_pendingLocalCandidates)
            {
                if (!_localCandidateFlowStarted)
                {
                    // Buffered until the owner is listening - see the field comment.
                    _pendingLocalCandidates.Add(candidate);
                    return;
                }
            }

            LocalIceCandidate?.Invoke(candidate);
        };

        _pc.onicecandidateerror += (candidate, error)
            => Diagnostic?.Invoke($"ICE candidate error: {error}");

        _pc.oniceconnectionstatechange += state
            => Diagnostic?.Invoke($"ICE connection state: {state}");

        _pc.onicegatheringstatechange += state =>
        {
            Diagnostic?.Invoke($"ICE gathering state: {state}");

            if (state == RTCIceGatheringState.complete)
            {
                Diagnostic?.Invoke(
                    $"Gathering finished: {_localCandidateCount} local candidate(s). " +
                    "Zero here means no usable network interface was found.");
            }
        };

        _pc.onconnectionstatechange += state =>
        {
            Diagnostic?.Invoke($"Peer connection state: {state}");

            if (state == RTCPeerConnectionState.connected)
            {
                // Microphone, speaker and camera are all opened HERE, not
                // earlier. By this point both codecs have been negotiated and
                // applied, which is what WindowsAudioEndPoint and
                // WindowsVideoEndPoint need before they initialise their
                // devices. Starting them before negotiation produces a call
                // that connects perfectly and carries no media at all.
                _ = _media.StartDevicesAsync();

                // The far end has just started receiving; without a keyframe it
                // would stare at a grey rectangle until the next scheduled one.
                _media.ForceKeyFrame();
            }

            ConnectionStateChanged?.Invoke(state);
        };
    }

    // First-sample flags. These make the log say which direction is broken:
    // no "sending audio" means capture/encode never produced anything;
    // no "receiving audio" means it is not arriving over the network.
    private bool _sentFirstAudio;
    private bool _sentFirstVideo;
    private bool _gotFirstAudio;
    private bool _gotFirstVideo;

    private void OnEncodedAudio(uint durationRtpUnits, byte[] sample)
    {
        if (_closed)
        {
            return;
        }

        if (!_sentFirstAudio)
        {
            _sentFirstAudio = true;
            Diagnostic?.Invoke($"Sending audio (first packet, {sample.Length} bytes).");
        }

        try { _pc.SendAudio(durationRtpUnits, sample); }
        catch (Exception ex) { Diagnostic?.Invoke($"SendAudio failed: {ex.Message}"); }
    }

    private void OnEncodedVideo(uint durationRtpUnits, byte[] sample)
    {
        if (_closed)
        {
            return;
        }

        if (!_sentFirstVideo)
        {
            _sentFirstVideo = true;
            Diagnostic?.Invoke($"Sending video (first frame, {sample.Length} bytes).");
        }

        try { _pc.SendVideo(durationRtpUnits, sample); }
        catch (Exception ex) { Diagnostic?.Invoke($"SendVideo failed: {ex.Message}"); }
    }

    private void OnAudioFrameReceived(EncodedAudioFrame frame)
    {
        if (!_gotFirstAudio)
        {
            _gotFirstAudio = true;
            Diagnostic?.Invoke("Receiving audio (first packet).");
        }

        _media.PlayRemoteAudio(frame);
    }

    private void OnVideoFrameReceived(IPEndPoint remote, uint timestamp, byte[] frame, VideoFormat format)
    {
        if (!_gotFirstVideo)
        {
            _gotFirstVideo = true;
            Diagnostic?.Invoke($"Receiving video (first frame, {frame?.Length ?? 0} bytes).");
        }

        _media.RenderRemoteVideo(frame);
    }

    // ------------------------------------------------------------ negotiation

    /// <summary>
    /// Call this once the owner has subscribed to <see cref="LocalIceCandidate"/>.
    /// Releases any candidates gathered in the meantime.
    /// </summary>
    public void StartLocalCandidateFlow()
    {
        // Logged here rather than in the constructor, because nothing is
        // subscribed to Diagnostic until the owner has finished wiring up.
        Diagnostic?.Invoke(
            "ICE servers configured: 0. No STUN, no TURN - host candidates only.");

        RTCIceCandidate[] buffered;

        lock (_pendingLocalCandidates)
        {
            _localCandidateFlowStarted = true;
            buffered = _pendingLocalCandidates.ToArray();
            _pendingLocalCandidates.Clear();
        }

        if (buffered.Length > 0)
        {
            Diagnostic?.Invoke($"Releasing {buffered.Length} buffered local candidate(s).");
        }

        foreach (var candidate in buffered)
        {
            LocalIceCandidate?.Invoke(candidate);
        }
    }

    /// <summary>Caller side. Produces the offer to send through the hub.</summary>
    public async Task<RTCSessionDescriptionInit> CreateOfferAsync()
    {
        // createOffer is synchronous and can block while ICE settles, so keep
        // it off the UI thread.
        var offer = await Task.Run(() => _pc.createOffer());
        await _pc.setLocalDescription(offer);
        return offer;
    }

    /// <summary>Callee side. Applies the offer and produces the answer.</summary>
    public async Task<RTCSessionDescriptionInit> CreateAnswerAsync()
    {
        var answer = await Task.Run(() => _pc.createAnswer());
        await _pc.setLocalDescription(answer);
        return answer;
    }

    /// <summary>Applies a remote offer or answer. Returns false if it was rejected.</summary>
    public bool SetRemoteDescription(string type, string sdp)
    {
        var sdpType = string.Equals(type, "offer", StringComparison.OrdinalIgnoreCase)
            ? RTCSdpType.offer
            : RTCSdpType.answer;

        var result = _pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = sdpType,
            sdp = sdp
        });

        if (result != SetDescriptionResultEnum.OK)
        {
            Diagnostic?.Invoke($"Remote {sdpType} rejected: {result}");
            return false;
        }

        _remoteDescriptionSet = true;
        FlushPendingCandidates();
        return true;
    }

    /// <summary>Adds a candidate trickled from the peer, queueing it if we are not ready.</summary>
    public void AddRemoteIceCandidate(string candidate, string? sdpMid, int sdpMLineIndex, string? usernameFragment)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var init = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = (ushort)Math.Max(0, sdpMLineIndex),
            usernameFragment = usernameFragment
        };

        if (!_remoteDescriptionSet)
        {
            lock (_pendingRemoteCandidates)
            {
                _pendingRemoteCandidates.Add(init);
            }

            return;
        }

        TryAddCandidate(init);
    }

    private void FlushPendingCandidates()
    {
        RTCIceCandidateInit[] pending;
        lock (_pendingRemoteCandidates)
        {
            pending = _pendingRemoteCandidates.ToArray();
            _pendingRemoteCandidates.Clear();
        }

        foreach (var candidate in pending)
        {
            TryAddCandidate(candidate);
        }
    }

    private void TryAddCandidate(RTCIceCandidateInit init)
    {
        var kind = GetCandidateType(init.candidate);

        // Refuse anything the far end may have gathered from a STUN or TURN
        // server. Accepting a relay candidate would route this call's media
        // through a third party, which is precisely what this project rules
        // out. Dropping it leaves the host candidates, which are all an
        // intranet needs.
        if (!IsHostCandidate(kind))
        {
            Diagnostic?.Invoke(
                $"Dropped remote '{kind}' candidate - only host candidates are accepted.");
            return;
        }

        try
        {
            _pc.addIceCandidate(init);
            _remoteCandidateCount++;
            Diagnostic?.Invoke($"Remote candidate #{_remoteCandidateCount} [{kind}]: {init.candidate}");
        }
        catch (Exception ex)
        {
            Diagnostic?.Invoke($"Rejected remote candidate: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the type token from an SDP candidate line. The grammar is
    /// "candidate:&lt;foundation&gt; &lt;component&gt; &lt;transport&gt; &lt;priority&gt;
    /// &lt;ip&gt; &lt;port&gt; typ &lt;type&gt; ...", so the value follows the literal "typ".
    /// </summary>
    private static string GetCandidateType(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "unknown";
        }

        var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "typ", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 1].ToLowerInvariant();
            }
        }

        return "unknown";
    }

    /// <summary>
    /// True for a local-interface candidate. "unknown" is allowed through so a
    /// parsing quirk cannot silently break calls; srflx (STUN) and relay (TURN)
    /// are the types this project rejects.
    /// </summary>
    private static bool IsHostCandidate(string kind)
        => kind is "host" or "unknown";

    // -------------------------------------------------------------- teardown

    public ValueTask DisposeAsync()
    {
        if (_closed)
        {
            return ValueTask.CompletedTask;
        }

        _closed = true;

        _media.EncodedAudioReady -= OnEncodedAudio;
        _media.EncodedVideoReady -= OnEncodedVideo;
        _pc.OnAudioFrameReceived -= OnAudioFrameReceived;
        _pc.OnVideoFrameReceived -= OnVideoFrameReceived;

        try { _pc.close(); } catch { /* already torn down */ }
        try { _pc.Dispose(); } catch { }

        return ValueTask.CompletedTask;
    }
}
