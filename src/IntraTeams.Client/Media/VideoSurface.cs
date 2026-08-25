using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SIPSorceryMedia.Abstractions;

namespace IntraTeams.Client.Media;

/// <summary>
/// Wraps a <see cref="WriteableBitmap"/> that video frames are blitted into.
///
/// Frames arrive on capture and decode threads at up to 30 fps, so this class
/// does two things carefully: it marshals to the UI thread, and it reallocates
/// the bitmap only when the frame dimensions actually change. Allocating a new
/// bitmap per frame is the usual cause of a WPF video view stuttering.
/// </summary>
public sealed class VideoSurface
{
    private readonly object _gate = new();

    private WriteableBitmap? _bitmap;
    private int _width;
    private int _height;

    /// <summary>Reused so a channel swap does not allocate on every frame.</summary>
    private byte[]? _conversionBuffer;

    /// <summary>Set to false while a marshalled update is still outstanding, to avoid queue build-up.</summary>
    private volatile bool _updatePending;

    public ImageSource? Source => _bitmap;

    public event Action? SourceChanged;

    public bool HasFrames { get; private set; }

    /// <summary>
    /// Pushes a frame. Safe to call from any thread; returns immediately.
    /// The caller's buffer is copied, so it may be reused after this returns.
    /// </summary>
    public void Update(byte[] frame, int width, int height, int stride, VideoPixelFormatsEnum pixelFormat)
    {
        if (frame is null || width <= 0 || height <= 0)
        {
            return;
        }

        // If the UI thread has not drained the previous frame yet, drop this one.
        // Showing the newest frame late is worse than skipping it.
        if (_updatePending)
        {
            return;
        }

        byte[] bgr;
        int bgrStride;

        try
        {
            bgr = ConvertToBgr24(frame, width, height, stride, pixelFormat, out bgrStride);
        }
        catch
        {
            return;
        }

        // Copy, because the source buffer is reused by the capture loop.
        var snapshot = new byte[bgr.Length];
        Buffer.BlockCopy(bgr, 0, snapshot, 0, bgr.Length);

        _updatePending = true;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _updatePending = false;
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                ApplyOnUiThread(snapshot, width, height, bgrStride);
            }
            finally
            {
                _updatePending = false;
            }
        }));
    }

    private void ApplyOnUiThread(byte[] bgr, int width, int height, int stride)
    {
        lock (_gate)
        {
            if (_bitmap is null || _width != width || _height != height)
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
                _width = width;
                _height = height;
                SourceChanged?.Invoke();
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), bgr, stride, 0);
            HasFrames = true;
        }
    }

    /// <summary>Clears the surface so a finished call does not leave the last frame frozen on screen.</summary>
    public void Clear()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            lock (_gate)
            {
                _bitmap = null;
                _width = 0;
                _height = 0;
                HasFrames = false;
                SourceChanged?.Invoke();
            }
        }));
    }

    /// <summary>
    /// Normalises whatever the codec or capture device produced into packed
    /// 24bpp BGR, which is what PixelFormats.Bgr24 expects.
    /// </summary>
    private byte[] ConvertToBgr24(
        byte[] source, int width, int height, int stride,
        VideoPixelFormatsEnum pixelFormat, out int outputStride)
    {
        outputStride = width * 3;
        var required = outputStride * height;

        switch (pixelFormat)
        {
            case VideoPixelFormatsEnum.Bgr:
            {
                // Already correct; only repack if the source rows are padded.
                if (stride <= 0 || stride == outputStride)
                {
                    return source;
                }

                var packed = Rent(required);
                for (var y = 0; y < height; y++)
                {
                    Buffer.BlockCopy(source, y * stride, packed, y * outputStride, outputStride);
                }

                return packed;
            }

            case VideoPixelFormatsEnum.Rgb:
            {
                var sourceStride = stride > 0 ? stride : width * 3;
                var swapped = Rent(required);

                for (var y = 0; y < height; y++)
                {
                    var sourceRow = y * sourceStride;
                    var targetRow = y * outputStride;

                    for (var x = 0; x < width; x++)
                    {
                        var s = sourceRow + (x * 3);
                        var t = targetRow + (x * 3);
                        swapped[t] = source[s + 2];
                        swapped[t + 1] = source[s + 1];
                        swapped[t + 2] = source[s];
                    }
                }

                return swapped;
            }

            case VideoPixelFormatsEnum.Bgra:
            case VideoPixelFormatsEnum.Rgba:
            {
                var sourceStride = stride > 0 ? stride : width * 4;
                var isRgba = pixelFormat == VideoPixelFormatsEnum.Rgba;
                var packed = Rent(required);

                for (var y = 0; y < height; y++)
                {
                    var sourceRow = y * sourceStride;
                    var targetRow = y * outputStride;

                    for (var x = 0; x < width; x++)
                    {
                        var s = sourceRow + (x * 4);
                        var t = targetRow + (x * 3);

                        if (isRgba)
                        {
                            packed[t] = source[s + 2];
                            packed[t + 1] = source[s + 1];
                            packed[t + 2] = source[s];
                        }
                        else
                        {
                            packed[t] = source[s];
                            packed[t + 1] = source[s + 1];
                            packed[t + 2] = source[s + 2];
                        }
                    }
                }

                return packed;
            }

            default:
                // I420 / NV12 would need a full colour-space conversion. The VP8
                // decoder in this app is asked for BGR, so this should not occur.
                throw new NotSupportedException($"Pixel format {pixelFormat} is not handled.");
        }
    }

    private byte[] Rent(int size)
    {
        if (_conversionBuffer is null || _conversionBuffer.Length < size)
        {
            _conversionBuffer = new byte[size];
        }

        return _conversionBuffer;
    }
}
