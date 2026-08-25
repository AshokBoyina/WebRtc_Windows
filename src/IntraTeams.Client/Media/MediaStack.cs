using System.Diagnostics;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

// The pure-managed VP8 codec lives in SIPSorcery.VP8. If the compiler cannot
// find VP8Codec, this using is the ONE line to correct - see README
// "If the VP8 type does not resolve".
using Vpx.Net;

namespace IntraTeams.Client.Media;

/// <summary>Which video source is currently feeding the outgoing video track.</summary>
public enum VideoSourceKind
{
    None = 0,
    Camera = 1,
    Screen = 2
}

/// <summary>
/// Owns every capture and playback device for one call, plus the VP8
/// encode/decode path.
///
/// There is exactly ONE outgoing video track. Camera and screen share it, and
/// "share screen" swaps which producer is running, so no renegotiation is ever
/// needed when the user toggles it.
///
/// Both producers hand RAW frames to this class, which encodes them with a
/// single VP8Codec. The camera endpoint is deliberately NOT allowed to encode:
/// its internal encoder rejects any frame whose dimensions are not multiples of
/// 16, and webcams routinely negotiate sizes like 640x360 that violate that.
/// Encoding here means <see cref="CropToMultipleOf16"/> can fix the geometry
/// first - see the comment on that method.
///
/// ORDERING RULE: devices are opened only after the codec has been negotiated,
/// via <see cref="StartDevicesAsync"/>, which PeerSession calls on the
/// transition to "connected". Opening them earlier leaves them configured
/// against a format that was not yet agreed - which presents as a call that
/// connects cleanly and carries no media.
/// </summary>
public sealed class MediaStack : IAsyncDisposable
{
    private const int VideoClockRate = 90000;

    /// <summary>Shared by camera and screen so there is one keyframe cadence.</summary>
    private readonly VP8Codec _encoder = new();

    /// <summary>Decoder for everything arriving from the far end.</summary>
    private readonly VP8Codec _decoder = new();

    private readonly Infrastructure.AppConfig _config;

    private WindowsAudioEndPoint? _audio;
    private WindowsVideoEndPoint? _camera;
    private GdiScreenCapture? _screen;

    private bool _audioStarted;

    /// <summary>
    /// Remembered so it can be applied to the camera endpoint whenever it is
    /// created - including when the user switches the camera on mid-call, long
    /// after negotiation happened.
    /// </summary>
    private VideoFormat? _negotiatedVideoFormat;

    /// <summary>Guards the encoder, which is not safe to call re-entrantly.</summary>
    private readonly object _encodeLock = new();

    /// <summary>Drops frames rather than queueing them when encoding falls behind.</summary>
    private int _encodeInFlight;

    private readonly Stopwatch _videoClock = Stopwatch.StartNew();
    private TimeSpan _lastVideoTimestamp = TimeSpan.Zero;

    /// <summary>So a per-frame failure is reported once, not thirty times a second.</summary>
    private string? _lastVideoErrorReported;
    private bool _croppingReported;
    private bool _unsupportedFormatReported;

    public MediaStack(Infrastructure.AppConfig config) => _config = config;

    // ------------------------------------------------------------------ state

    private VideoSourceKind _activeVideoSource = VideoSourceKind.None;

    public VideoSourceKind ActiveVideoSource
    {
        get => _activeVideoSource;
        private set
        {
            if (_activeVideoSource == value)
            {
                return;
            }

            _activeVideoSource = value;
            VideoSourceChanged?.Invoke(value);
        }
    }

    /// <summary>When true, captured audio is discarded instead of sent.</summary>
    public bool IsMicrophoneMuted { get; set; }

    /// <summary>
    /// Set before connecting to ask for the camera to come up as soon as the
    /// peer connection is established.
    /// </summary>
    public bool CameraRequested { get; set; }

    public bool HasCamera { get; private set; }

    // ----------------------------------------------------------------- events

    /// <summary>Encoded audio ready for the peer connection. (durationRtpUnits, sample)</summary>
    public event Action<uint, byte[]>? EncodedAudioReady;

    /// <summary>Encoded VP8 ready for the peer connection. (durationRtpUnits, sample)</summary>
    public event Action<uint, byte[]>? EncodedVideoReady;

    /// <summary>Local preview frame. (buffer, width, height, pixelFormat)</summary>
    public event Action<byte[], int, int, VideoPixelFormatsEnum>? LocalFrameReady;

    /// <summary>Decoded frame from the far end. (buffer, width, height, stride, pixelFormat)</summary>
    public event Action<byte[], int, int, int, VideoPixelFormatsEnum>? RemoteFrameReady;

    /// <summary>Raised whenever the active video producer changes, so the UI can resync.</summary>
    public event Action<VideoSourceKind>? VideoSourceChanged;

    public event Action<string>? Error;

    /// <summary>Non-error progress messages for the Connection log.</summary>
    public event Action<string>? Info;

    // ---------------------------------------------------------- device startup

    /// <summary>
    /// Opens the devices for this call. Call ONLY after codec negotiation -
    /// PeerSession does this on the transition to "connected".
    /// </summary>
    public async Task StartDevicesAsync()
    {
        await StartAudioAsync();

        if (CameraRequested && ActiveVideoSource == VideoSourceKind.None)
        {
            await StartCameraAsync();
        }
    }

    // ------------------------------------------------------------------ audio

    /// <summary>Formats offered for the outgoing audio track.</summary>
    public List<AudioFormat> GetAudioFormats()
    {
        EnsureAudio();
        return _audio!.GetAudioSourceFormats();
    }

    /// <summary>VP8 only. Declared explicitly so no camera is needed to build the track.</summary>
    public static List<VideoFormat> GetVideoFormats() =>
        new() { new VideoFormat(VideoCodecsEnum.VP8, 96, VideoClockRate) };

    private void EnsureAudio()
    {
        if (_audio is not null)
        {
            return;
        }

        // AudioEncoder() gives G711 (PCMU/PCMA) and G722 - plenty for a LAN and
        // free of any native dependency.
        _audio = new WindowsAudioEndPoint(new AudioEncoder());

        _audio.OnAudioSourceEncodedSample += (durationRtpUnits, sample) =>
        {
            if (!IsMicrophoneMuted)
            {
                EncodedAudioReady?.Invoke(durationRtpUnits, sample);
            }
        };

        _audio.OnAudioSourceError += message => Error?.Invoke($"Microphone: {message}");
        _audio.OnAudioSinkError += message => Error?.Invoke($"Speaker: {message}");
    }

    /// <summary>
    /// Opens the microphone and speaker. Must run AFTER the codec has been
    /// negotiated and applied - see the ORDERING RULE on this class.
    /// </summary>
    public async Task StartAudioAsync()
    {
        EnsureAudio();

        // Idempotent: the connected state can be entered more than once.
        if (_audioStarted)
        {
            return;
        }

        _audioStarted = true;

        try
        {
            var formats = _audio!.GetAudioSourceFormats();
            Info?.Invoke(formats.Count == 0
                ? "WARNING: the microphone offered no audio formats - no capture device?"
                : $"Audio source formats: {string.Join(", ", formats.Select(f => f.Codec))}");

            await _audio.StartAudio();
            await _audio.StartAudioSink();

            Info?.Invoke("Microphone and speaker started (after codec negotiation).");
        }
        catch (Exception ex)
        {
            _audioStarted = false;
            Error?.Invoke($"Could not start audio devices: {ex.Message}");
        }
    }

    public void SetNegotiatedAudioFormat(AudioFormat format)
    {
        EnsureAudio();

        try
        {
            _audio!.SetAudioSourceFormat(format);
            _audio.SetAudioSinkFormat(format);

            Info?.Invoke(
                $"Audio format applied to devices: {format.Codec} " +
                $"@ {format.ClockRate} Hz, {format.ChannelCount} ch.");
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Could not apply the negotiated audio format: {ex.Message}");
        }
    }

    /// <summary>Hands an incoming encoded audio frame to the speaker.</summary>
    public void PlayRemoteAudio(EncodedAudioFrame frame)
    {
        try
        {
            _audio?.GotEncodedMediaFrame(frame);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Audio playback: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ video

    public void SetNegotiatedVideoFormat(VideoFormat format)
    {
        _negotiatedVideoFormat = format;
        ApplyVideoFormatToCamera();
        Info?.Invoke($"Video format recorded: {format.Codec} (payload {format.FormatID}).");
    }

    private void ApplyVideoFormatToCamera()
    {
        if (_camera is null || _negotiatedVideoFormat is null)
        {
            return;
        }

        try
        {
            _camera.SetVideoSourceFormat(_negotiatedVideoFormat.Value);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Could not apply the video format to the camera: {ex.Message}");
        }
    }

    /// <summary>Decodes an incoming VP8 frame and raises <see cref="RemoteFrameReady"/>.</summary>
    public void RenderRemoteVideo(byte[]? encodedFrame)
    {
        if (encodedFrame is not { Length: > 0 })
        {
            return;
        }

        try
        {
            // Use DecodeVideo, NOT DecodeVideoFaster: the latter is declared on
            // IVideoEncoder but throws NotImplementedException in SIPSorcery.VP8.
            // DecodeVideo ignores the pixelFormat argument entirely and always
            // returns tightly packed BGR, so the stride is always width * 3.
            foreach (var sample in _decoder.DecodeVideo(
                         encodedFrame, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.VP8))
            {
                // VideoSample is a STRUCT, so there is no null to test for on
                // the sample itself - only its buffer can be null.
                if (sample.Sample is not { Length: > 0 })
                {
                    continue;
                }

                var width = (int)sample.Width;
                var height = (int)sample.Height;

                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                var stride = width * 3;

                if (sample.Sample.Length < stride * height)
                {
                    continue;
                }

                RemoteFrameReady?.Invoke(
                    sample.Sample, width, height, stride, VideoPixelFormatsEnum.Bgr);
            }
        }
        catch (Exception ex)
        {
            ReportVideoError($"Video decode: {ex.Message}");
        }
    }

    // ----------------------------------------------------------------- camera

    public async Task<bool> StartCameraAsync()
    {
        if (ActiveVideoSource == VideoSourceKind.Camera)
        {
            return true;
        }

        StopScreenShare();

        try
        {
            if (_camera is null)
            {
                // The encoder handed over is required by the constructor but is
                // never driven, because nothing subscribes to the endpoint's
                // OnVideoSourceEncodedSample. That is deliberate: its encoder
                // throws on any frame whose dimensions are not multiples of 16,
                // and we cannot dictate what the webcam negotiates. Raw frames
                // come here instead and are cropped before encoding.
                _camera = new WindowsVideoEndPoint(
                    new VP8Codec(),
                    string.IsNullOrWhiteSpace(_config.CameraDeviceId) ? null : _config.CameraDeviceId,
                    (uint)_config.CameraWidth,
                    (uint)_config.CameraHeight,
                    (uint)_config.CameraFps);

                _camera.OnVideoSourceRawSample += OnCameraRawSample;
                _camera.OnVideoSourceError += message => Error?.Invoke($"Camera: {message}");

                if (!await _camera.InitialiseVideoSourceDevice())
                {
                    Error?.Invoke("No usable camera was found.");
                    await DisposeCameraAsync();
                    HasCamera = false;
                    return false;
                }

                Info?.Invoke("Camera device initialised.");
            }

            ApplyVideoFormatToCamera();

            await _camera.StartVideo();

            HasCamera = true;
            ActiveVideoSource = VideoSourceKind.Camera;
            ForceKeyFrame();

            Info?.Invoke("Camera started.");
            return true;
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Could not start the camera: {ex.Message}");
            await DisposeCameraAsync();
            HasCamera = false;
            return false;
        }
    }

    public async Task StopCameraAsync()
    {
        if (_camera is not null)
        {
            try { await _camera.CloseVideo(); } catch { /* device already gone */ }
        }

        if (ActiveVideoSource == VideoSourceKind.Camera)
        {
            ActiveVideoSource = VideoSourceKind.None;
        }
    }

    private void OnCameraRawSample(uint durationMilliseconds, int width, int height,
        byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        if (ActiveVideoSource == VideoSourceKind.Camera)
        {
            EncodeAndSend(sample, width, height, pixelFormat);
        }
    }

    // ----------------------------------------------------------- screen share

    public void StartScreenShare()
    {
        if (ActiveVideoSource == VideoSourceKind.Screen)
        {
            return;
        }

        // Fire and forget: stopping the camera must not block the UI thread.
        _ = StopCameraAsync();

        _screen ??= new GdiScreenCapture(_config.ScreenShareWidth, _config.ScreenShareFps);
        _screen.FrameCaptured -= OnScreenFrame;
        _screen.FrameCaptured += OnScreenFrame;
        _screen.Start();

        ActiveVideoSource = VideoSourceKind.Screen;
        ForceKeyFrame();

        Info?.Invoke($"Screen capture started at {_config.ScreenShareWidth}px / {_config.ScreenShareFps} fps.");
    }

    public void StopScreenShare()
    {
        if (_screen is not null)
        {
            _screen.FrameCaptured -= OnScreenFrame;
            _screen.Stop();
        }

        if (ActiveVideoSource == VideoSourceKind.Screen)
        {
            ActiveVideoSource = VideoSourceKind.None;
        }
    }

    private void OnScreenFrame(byte[] bgr, int width, int height)
    {
        if (ActiveVideoSource == VideoSourceKind.Screen)
        {
            EncodeAndSend(bgr, width, height, VideoPixelFormatsEnum.Bgr);
        }
    }

    // --------------------------------------------------------------- encoding

    private void EncodeAndSend(byte[] frame, int width, int height, VideoPixelFormatsEnum pixelFormat)
    {
        if (frame is not { Length: > 0 } || width <= 0 || height <= 0)
        {
            return;
        }

        // VP8 requires both dimensions to be positive multiples of 16. Webcams
        // routinely negotiate sizes that are not - 640x360 is extremely common,
        // and 360 is not a multiple of 16. Crop before encoding.
        if (!TryCropToMultipleOf16(frame, width, height, pixelFormat,
                out var cropped, out var cropWidth, out var cropHeight))
        {
            return;
        }

        // Show the user their own outgoing picture, even if encoding is
        // saturated and this frame is about to be dropped.
        LocalFrameReady?.Invoke(cropped, cropWidth, cropHeight, pixelFormat);

        // Drop rather than queue: a backlog would show up as growing latency.
        if (Interlocked.CompareExchange(ref _encodeInFlight, 1, 0) != 0)
        {
            return;
        }

        try
        {
            byte[]? encoded;
            lock (_encodeLock)
            {
                encoded = _encoder.EncodeVideo(
                    cropWidth, cropHeight, cropped, pixelFormat, VideoCodecsEnum.VP8);
            }

            if (encoded is not { Length: > 0 })
            {
                return;
            }

            // RTP timestamps advance by real elapsed time at the 90 kHz video
            // clock, so a variable capture rate still plays back smoothly.
            var now = _videoClock.Elapsed;
            var delta = now - _lastVideoTimestamp;
            _lastVideoTimestamp = now;

            var durationRtpUnits = (uint)Math.Clamp(
                delta.TotalSeconds * VideoClockRate, 1, VideoClockRate);

            EncodedVideoReady?.Invoke(durationRtpUnits, encoded);
        }
        catch (Exception ex)
        {
            // Never let a per-frame fault escape onto a capture thread: at 20 fps
            // that becomes an unhandled-exception storm.
            ReportVideoError($"Video encode: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _encodeInFlight, 0);
        }
    }

    /// <summary>
    /// Trims a frame down to the largest centred region whose width and height
    /// are both multiples of 16, which is what the VP8 encoder demands.
    ///
    /// A 640x360 webcam frame becomes 640x352 - eight rows lost, four off each
    /// edge. Cropping is preferred over scaling because it costs almost nothing
    /// per frame and introduces no blur.
    /// </summary>
    private bool TryCropToMultipleOf16(
        byte[] source, int width, int height, VideoPixelFormatsEnum pixelFormat,
        out byte[] result, out int outWidth, out int outHeight)
    {
        outWidth = width - (width % 16);
        outHeight = height - (height % 16);
        result = source;

        if (outWidth <= 0 || outHeight <= 0)
        {
            ReportVideoError($"Frame {width}x{height} is too small to encode.");
            return false;
        }

        if (outWidth == width && outHeight == height)
        {
            return true; // Already legal, no copy needed.
        }

        if (!_croppingReported)
        {
            _croppingReported = true;
            Info?.Invoke(
                $"Cropping video from {width}x{height} to {outWidth}x{outHeight} " +
                "so the dimensions are multiples of 16 (VP8 requirement).");
        }

        // Keep offsets even so chroma subsampling stays aligned.
        var xOffset = ((width - outWidth) / 2) & ~1;
        var yOffset = ((height - outHeight) / 2) & ~1;

        switch (pixelFormat)
        {
            case VideoPixelFormatsEnum.Bgr:
            case VideoPixelFormatsEnum.Rgb:
            case VideoPixelFormatsEnum.Bgra:
            case VideoPixelFormatsEnum.Rgba:
            {
                var bpp = pixelFormat is VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba ? 4 : 3;
                var sourceStride = width * bpp;
                var targetStride = outWidth * bpp;

                if (source.Length < sourceStride * height)
                {
                    return false;
                }

                var buffer = new byte[targetStride * outHeight];

                for (var y = 0; y < outHeight; y++)
                {
                    Buffer.BlockCopy(
                        source, ((y + yOffset) * sourceStride) + (xOffset * bpp),
                        buffer, y * targetStride,
                        targetStride);
                }

                result = buffer;
                return true;
            }

            case VideoPixelFormatsEnum.I420:
            {
                // Planar Y, then U and V at half width and half height.
                var ySize = width * height;
                var chromaWidth = width / 2;
                var chromaHeight = height / 2;

                if (source.Length < ySize + (2 * chromaWidth * chromaHeight))
                {
                    return false;
                }

                var outChromaWidth = outWidth / 2;
                var outChromaHeight = outHeight / 2;
                var buffer = new byte[(outWidth * outHeight) + (2 * outChromaWidth * outChromaHeight)];

                for (var y = 0; y < outHeight; y++)
                {
                    Buffer.BlockCopy(source, ((y + yOffset) * width) + xOffset,
                        buffer, y * outWidth, outWidth);
                }

                var uSource = ySize;
                var vSource = ySize + (chromaWidth * chromaHeight);
                var uTarget = outWidth * outHeight;
                var vTarget = uTarget + (outChromaWidth * outChromaHeight);

                for (var y = 0; y < outChromaHeight; y++)
                {
                    Buffer.BlockCopy(source, uSource + (((y + (yOffset / 2)) * chromaWidth) + (xOffset / 2)),
                        buffer, uTarget + (y * outChromaWidth), outChromaWidth);

                    Buffer.BlockCopy(source, vSource + (((y + (yOffset / 2)) * chromaWidth) + (xOffset / 2)),
                        buffer, vTarget + (y * outChromaWidth), outChromaWidth);
                }

                result = buffer;
                return true;
            }

            case VideoPixelFormatsEnum.NV12:
            {
                // Planar Y, then a single interleaved UV plane at half height.
                var ySize = width * height;

                if (source.Length < ySize + (ySize / 2))
                {
                    return false;
                }

                var outUvHeight = outHeight / 2;
                var buffer = new byte[(outWidth * outHeight) + (outWidth * outUvHeight)];

                for (var y = 0; y < outHeight; y++)
                {
                    Buffer.BlockCopy(source, ((y + yOffset) * width) + xOffset,
                        buffer, y * outWidth, outWidth);
                }

                var uvTarget = outWidth * outHeight;

                for (var y = 0; y < outUvHeight; y++)
                {
                    Buffer.BlockCopy(source, ySize + (((y + (yOffset / 2)) * width) + xOffset),
                        buffer, uvTarget + (y * outWidth), outWidth);
                }

                result = buffer;
                return true;
            }

            default:
                if (!_unsupportedFormatReported)
                {
                    _unsupportedFormatReported = true;
                    ReportVideoError($"Cannot crop pixel format {pixelFormat}; video will not be sent.");
                }

                return false;
        }
    }

    /// <summary>
    /// Reports a video fault at most once per distinct message. Video errors
    /// repeat every frame, and an unfiltered report becomes a flood.
    /// </summary>
    private void ReportVideoError(string message)
    {
        if (_lastVideoErrorReported == message)
        {
            return;
        }

        _lastVideoErrorReported = message;
        Error?.Invoke(message);
    }

    /// <summary>
    /// Requests an immediate keyframe. Needed whenever the picture changes
    /// wholesale - camera to screen, or the peer having only just started
    /// receiving - otherwise the far end shows garbage until the next one.
    /// </summary>
    public void ForceKeyFrame()
    {
        try
        {
            lock (_encodeLock)
            {
                _encoder.ForceKeyFrame();
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Keyframe request: {ex.Message}");
        }

        // Note: the camera endpoint's own encoder is never asked for a keyframe.
        // Doing so can make it encode a frame, which is exactly the code path
        // that throws on non-multiple-of-16 dimensions.
    }

    // -------------------------------------------------------------- teardown

    private async Task DisposeCameraAsync()
    {
        if (_camera is null)
        {
            return;
        }

        _camera.OnVideoSourceRawSample -= OnCameraRawSample;

        try { await _camera.CloseVideo(); } catch { }
        try { _camera.Dispose(); } catch { }

        _camera = null;
    }

    public async ValueTask DisposeAsync()
    {
        StopScreenShare();
        _screen?.Dispose();
        _screen = null;

        await DisposeCameraAsync();

        if (_audio is not null)
        {
            try { await _audio.CloseAudio(); } catch { }
            try { await _audio.CloseAudioSink(); } catch { }
            _audio = null;
        }

        try { _encoder.Dispose(); } catch { }
        try { _decoder.Dispose(); } catch { }

        ActiveVideoSource = VideoSourceKind.None;
    }
}
