// ---------------------------------------------------------------------------
// BUILD-TIME VERIFICATION ONLY. Not part of the shipped application.
//
// These are stand-in declarations of the third-party API surface (SIPSorcery,
// SIPSorceryMedia.*, SIPSorcery.VP8 and the SignalR client), transcribed from
// the published source of those libraries. They exist so the non-WPF client
// code can be compiled on a machine that cannot restore those NuGet packages,
// which catches ordinary C# mistakes: typos, wrong argument order, bad
// nullability, missing members.
//
// It does NOT prove the real libraries behave this way at runtime.
// ---------------------------------------------------------------------------

using System.Net;

namespace SIPSorceryMedia.Abstractions
{
    public enum VideoPixelFormatsEnum { Rgb = 0, Bgr = 1, Bgra = 2, I420 = 3, NV12 = 4, Rgba = 5 }

    public enum VideoCodecsEnum { VP8, H264, VP9, H265, JPEG }

    public enum AudioCodecsEnum { PCMU, PCMA, G722, G729, OPUS, PCM_S16LE, L16 }

    public enum AudioSamplingRatesEnum { Rate8KHz = 8000, Rate16KHz = 16000 }

    public struct AudioFormat
    {
        public AudioFormat(AudioCodecsEnum codec, int formatID, int clockRate = 8000,
            int channelCount = 1, string? parameters = null)
        {
            Codec = codec; FormatID = formatID; ClockRate = clockRate;
            RtpClockRate = clockRate; ChannelCount = channelCount;
            Parameters = parameters; FormatName = codec.ToString();
        }

        public AudioCodecsEnum Codec { get; set; }
        public int FormatID { get; set; }
        public string? FormatName { get; set; }
        public int RtpClockRate { get; set; }
        public int ClockRate { get; set; }
        public int ChannelCount { get; set; }
        public string? Parameters { get; set; }
        public bool IsEmpty() => false;
    }

    public struct VideoFormat
    {
        public VideoFormat(VideoCodecsEnum codec, int formatID, int clockRate = 90000, string? parameters = null)
        {
            Codec = codec; FormatID = formatID; ClockRate = clockRate;
            Parameters = parameters; FormatName = codec.ToString();
        }

        public VideoCodecsEnum Codec { get; set; }
        public int FormatID { get; set; }
        public string? FormatName { get; set; }
        public int ClockRate { get; set; }
        public string? Parameters { get; set; }
        public bool IsEmpty() => false;
    }

    public class RawImage
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public IntPtr Sample { get; set; }
        public VideoPixelFormatsEnum PixelFormat { get; set; }
        public byte[] GetBuffer() => Array.Empty<byte>();
    }

    // VideoSample is a STRUCT in the real library, not a class. Declared the
    // same way here so that a null-conditional access on it fails to compile.
    public struct VideoSample
    {
        public uint Width { get; set; }
        public uint Height { get; set; }
        public byte[]? Sample { get; set; }
    }

    public class EncodedAudioFrame
    {
        public uint DurationRtpUnits { get; set; }
        public AudioFormat AudioFormat { get; set; }
        public byte[]? EncodedAudio { get; set; }
    }

    public delegate void EncodedSampleDelegate(uint durationRtpUnits, byte[] sample);
    public delegate void SourceErrorDelegate(string errorMessage);
    public delegate void RawVideoSampleDelegate(uint durationMilliseconds, int width, int height,
        byte[] sample, VideoPixelFormatsEnum pixelFormat);
    public delegate void RawVideoSampleFasterDelegate(uint durationMilliseconds, RawImage rawImage);
    public delegate void VideoSinkSampleDecodedDelegate(byte[] sample, uint width, uint height,
        int stride, VideoPixelFormatsEnum pixelFormat);
    public delegate void VideoSinkSampleDecodedFasterDelegate(RawImage rawImage);
    public delegate void RawAudioSampleDelegate(AudioSamplingRatesEnum samplingRate,
        uint durationMilliseconds, short[] sample);

    public interface IAudioEncoder
    {
        List<AudioFormat> SupportedFormats { get; }
        byte[] EncodeAudio(short[] pcm, AudioFormat format);
        short[] DecodeAudio(byte[] encodedSample, AudioFormat format);
    }

    public interface IVideoEncoder : IDisposable
    {
        List<VideoFormat> SupportedFormats { get; }
        byte[] EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec);
        byte[] EncodeVideoFaster(RawImage rawImage, VideoCodecsEnum codec);
        void ForceKeyFrame();
        IEnumerable<VideoSample> DecodeVideo(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec);
        IEnumerable<RawImage> DecodeVideoFaster(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec);
    }

    public interface IVideoSource
    {
        event EncodedSampleDelegate OnVideoSourceEncodedSample;
        event RawVideoSampleDelegate OnVideoSourceRawSample;
        event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;
        event SourceErrorDelegate OnVideoSourceError;
        Task PauseVideo();
        Task ResumeVideo();
        Task StartVideo();
        Task CloseVideo();
        List<VideoFormat> GetVideoSourceFormats();
        void SetVideoSourceFormat(VideoFormat videoFormat);
        void RestrictFormats(Func<VideoFormat, bool> filter);
        void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height,
            byte[] sample, VideoPixelFormatsEnum pixelFormat);
        void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage);
        void ForceKeyFrame();
        bool HasEncodedVideoSubscribers();
        bool IsVideoSourcePaused();
    }

    public interface IVideoSink
    {
        event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;
        event VideoSinkSampleDecodedFasterDelegate OnVideoSinkDecodedSampleFaster;
        void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp,
            int payloadID, bool marker, byte[] payload);
        void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] payload, VideoFormat format);
        List<VideoFormat> GetVideoSinkFormats();
        void SetVideoSinkFormat(VideoFormat videoFormat);
        void RestrictFormats(Func<VideoFormat, bool> filter);
        Task PauseVideoSink();
        Task ResumeVideoSink();
        Task StartVideoSink();
        Task CloseVideoSink();
    }

    public interface IVideoEndPoint : IVideoSource, IVideoSink
    {
        Task Pause();
        Task Resume();
        Task Start();
        Task Close();
    }

    public class MediaEndPoints
    {
        public object? AudioSource { get; set; }
        public object? AudioSink { get; set; }
        public object? VideoSource { get; set; }
        public object? VideoSink { get; set; }
    }
}

namespace SIPSorcery.Media
{
    using SIPSorceryMedia.Abstractions;

    public class AudioEncoder : IAudioEncoder, IDisposable
    {
        public AudioEncoder(bool includeLinearFormats = false, bool includeOpus = false) { }
        public List<AudioFormat> SupportedFormats { get; } = new();
        public byte[] EncodeAudio(short[] pcm, AudioFormat format) => Array.Empty<byte>();
        public short[] DecodeAudio(byte[] encodedSample, AudioFormat format) => Array.Empty<short>();
        public void Dispose() { }
    }
}

namespace Vpx.Net
{
    using SIPSorceryMedia.Abstractions;

    public class VP8Codec : IVideoEncoder
    {
        public List<VideoFormat> SupportedFormats { get; } = new();
        public uint? TargetKbps { get; set; }
        public byte[] EncodeVideo(int width, int height, byte[] sample,
            VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec) => Array.Empty<byte>();
        // NOTE: the two "Faster" overloads exist on IVideoEncoder but throw
        // NotImplementedException in the real SIPSorcery.VP8 package. Mirrored
        // here so nobody reintroduces a call to them.
        public byte[] EncodeVideoFaster(RawImage rawImage, VideoCodecsEnum codec)
            => throw new NotImplementedException();
        public void ForceKeyFrame() { }
        public IEnumerable<VideoSample> DecodeVideo(byte[] frame, VideoPixelFormatsEnum pixelFormat,
            VideoCodecsEnum codec) => Array.Empty<VideoSample>();
        public IEnumerable<RawImage> DecodeVideoFaster(byte[] encodedSample,
            VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
            => throw new NotImplementedException();
        public void Dispose() { }
    }
}

namespace SIPSorceryMedia.Windows
{
    using SIPSorceryMedia.Abstractions;

    public struct VideoCaptureDeviceInfo
    {
        public string ID;
        public string Name;
    }

    public class WindowsAudioEndPoint
    {
        public WindowsAudioEndPoint(IAudioEncoder audioEncoder, int audioOutDeviceIndex = -1,
            int audioInDeviceIndex = -1, bool disableSource = false, bool disableSink = false) { }

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event SourceErrorDelegate? OnAudioSourceError;
        public event SourceErrorDelegate? OnAudioSinkError;

        public List<AudioFormat> GetAudioSourceFormats() => new();
        public List<AudioFormat> GetAudioSinkFormats() => new();
        public void SetAudioSourceFormat(AudioFormat audioFormat) { }
        public void SetAudioSinkFormat(AudioFormat audioFormat) { }
        public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame) { }
        public Task StartAudio() => Task.CompletedTask;
        public Task CloseAudio() => Task.CompletedTask;
        public Task PauseAudio() => Task.CompletedTask;
        public Task ResumeAudio() => Task.CompletedTask;
        public Task StartAudioSink() => Task.CompletedTask;
        public Task CloseAudioSink() => Task.CompletedTask;
    }

    public class WindowsVideoEndPoint : IDisposable
    {
        public WindowsVideoEndPoint(IVideoEncoder videoEncoder, string? videoDeviceID = null,
            uint width = 0, uint height = 0, uint fps = 0) { }

        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
        public event VideoSinkSampleDecodedDelegate? OnVideoSinkDecodedSample;
        public event SourceErrorDelegate? OnVideoSourceError;

        public List<VideoFormat> GetVideoSourceFormats() => new();
        public void SetVideoSourceFormat(VideoFormat videoFormat) { }
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height,
            byte[] sample, VideoPixelFormatsEnum pixelFormat) { }
        public void ForceKeyFrame() { }
        public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] frame, VideoFormat format) { }
        public Task<bool> InitialiseVideoSourceDevice() => Task.FromResult(true);
        public Task StartVideo() => Task.CompletedTask;
        public Task CloseVideo() => Task.CompletedTask;
        public void Dispose() { }

        public static Task<List<VideoCaptureDeviceInfo>> GetVideoCatpureDevices()
            => Task.FromResult(new List<VideoCaptureDeviceInfo>());
    }
}

namespace SIPSorcery.Net
{
    using SIPSorceryMedia.Abstractions;

    public enum RTCSdpType { answer = 0, offer = 1, pranswer = 2, rollback = 3 }
    public enum RTCIceTransportPolicy { all, relay }
    public enum RTCBundlePolicy { balanced, max_compat, max_bundle }
    public enum RTCSignalingState { stable, have_local_offer, have_remote_offer, closed }
    public enum RTCPeerConnectionState { closed, failed, disconnected, @new, connecting, connected }
    public enum RTCIceGatheringState { @new, gathering, complete }
    public enum RTCIceConnectionState { closed, failed, disconnected, @new, checking, connected }
    public enum RTCIceCandidateType { host, prflx, srflx, relay }
    public enum MediaStreamStatusEnum { SendRecv, SendOnly, RecvOnly, Inactive }
    public enum SDPMediaTypesEnum { audio, video, text, application }

    public enum SetDescriptionResultEnum
    {
        OK, Error, AudioIncompatible, VideoIncompatible, NoMatchingMediaType,
        NoRemoteMedia, NoLocalMedia, CryptoNegotiationFailed,
        DataChannelTransportNotSupported, DtlsFingerprintDigestNotSupported,
        DtlsFingerprintInvalid, DtlsFingerprintMissing, WrongSdpTypeOfferAfterOffer
    }

    public class RTCIceServer
    {
        public string? urls;
        public string? username;
        public string? credential;
    }

    public class RTCConfiguration
    {
        public List<RTCIceServer>? iceServers;
        public RTCIceTransportPolicy iceTransportPolicy;
        public RTCBundlePolicy bundlePolicy;
        public int iceCandidatePoolSize = 0;
        public IPAddress? X_BindAddress;
        public bool X_UseRtpFeedbackProfile;
        public bool X_ICEIncludeAllInterfaceAddresses;
        public bool X_UseRsaForDtlsCertificate;
        public int X_GatherTimeoutMs = 30000;
    }

    public class RTCOfferOptions { public bool X_ExcludeIceCandidates; }
    public class RTCAnswerOptions { public bool X_ExcludeIceCandidates; }

    public class RTCSessionDescriptionInit
    {
        public RTCSdpType type { get; set; }
        public string? sdp { get; set; }
    }

    public class RTCIceCandidateInit
    {
        public string? candidate { get; set; }
        public string? sdpMid { get; set; }
        public ushort sdpMLineIndex { get; set; }
        public string? usernameFragment { get; set; }
    }

    public class RTCIceCandidate
    {
        public string candidate => "";
        public string sdpMid => "";
        public ushort sdpMLineIndex => 0;
        public string usernameFragment => "";
        public RTCIceCandidateType type;
    }

    public class MediaStreamTrack
    {
        public MediaStreamTrack(AudioFormat format, MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv) { }
        public MediaStreamTrack(List<AudioFormat> formats, MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv) { }
        public MediaStreamTrack(VideoFormat format, MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv) { }
        public MediaStreamTrack(List<VideoFormat> formats, MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv) { }
    }

    public class RTPSession : IDisposable
    {
        public event Action<List<AudioFormat>>? OnAudioFormatsNegotiated;
        public event Action<List<VideoFormat>>? OnVideoFormatsNegotiated;
        public event Action<EncodedAudioFrame>? OnAudioFrameReceived;
        public event Action<IPEndPoint, uint, byte[], VideoFormat>? OnVideoFrameReceived;
        public event Action<string>? OnRtpClosed;

        public virtual void addTrack(MediaStreamTrack track) { }
        public void SendAudio(uint durationRtpUnits, byte[] sample) { }
        public void SendVideo(uint durationRtpUnits, byte[] sample) { }
        public virtual void Close(string reason) { }
        public virtual void Dispose() { }
    }

    public class RTCPeerConnection : RTPSession
    {
        public RTCPeerConnection() { }
        public RTCPeerConnection(RTCConfiguration? configuration, int bindPort = 0) { }

        public event Action? onnegotiationneeded;
        public event Action<RTCIceCandidate>? onicecandidate;
        public event Action<RTCIceCandidate, string>? onicecandidateerror;
        public event Action? onsignalingstatechange;
        public event Action<RTCIceConnectionState>? oniceconnectionstatechange;
        public event Action<RTCIceGatheringState>? onicegatheringstatechange;
        public event Action<RTCPeerConnectionState>? onconnectionstatechange;

        public RTCSignalingState signalingState { get; private set; }
        public RTCPeerConnectionState connectionState { get; private set; }

        public RTCSessionDescriptionInit createOffer(RTCOfferOptions? options = null) => new();
        public RTCSessionDescriptionInit createAnswer(RTCAnswerOptions? options = null) => new();
        public Task setLocalDescription(RTCSessionDescriptionInit init) => Task.CompletedTask;
        public SetDescriptionResultEnum setRemoteDescription(RTCSessionDescriptionInit init) => SetDescriptionResultEnum.OK;
        public void addIceCandidate(RTCIceCandidateInit candidateInit) { }
        public void close() { }
    }
}

namespace Microsoft.AspNetCore.SignalR.Client
{
    public enum HubConnectionState { Disconnected, Connected, Connecting, Reconnecting }

    public class HubConnection : IAsyncDisposable
    {
        public string? ConnectionId => null;
        public HubConnectionState State => HubConnectionState.Disconnected;

        public event Func<Exception?, Task>? Reconnecting;
        public event Func<string?, Task>? Reconnected;
        public event Func<Exception?, Task>? Closed;

        public IDisposable On<T1>(string methodName, Action<T1> handler) => new Dummy();
        public IDisposable On<T1, T2>(string methodName, Action<T1, T2> handler) => new Dummy();

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendAsync(string methodName, params object?[] args) => Task.CompletedTask;
        public Task<TResult> InvokeAsync<TResult>(string methodName, params object?[] args)
            => Task.FromResult(default(TResult)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class Dummy : IDisposable { public void Dispose() { } }
    }

    public class HubConnectionBuilder
    {
        public HubConnectionBuilder WithUrl(string url) => this;
        public HubConnectionBuilder WithAutomaticReconnect(TimeSpan[] retryDelays) => this;
        public HubConnection Build() => new();
    }
}
