using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VideoPixelFormatsEnum = SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum;

namespace IntraTeams.WinForms.Controls;

/// <summary>
/// Renders raw video frames into a GDI+ bitmap, letterboxed to the control.
///
/// Frames arrive on capture and decode threads at up to 30 fps, so this
/// control is careful about two things, mirroring the WPF client's
/// VideoSurface:
///   - frames are converted off the UI thread and marshalled with BeginInvoke,
///     and a new frame is DROPPED while the previous one is still queued, so a
///     slow UI shows a late picture rather than a growing backlog;
///   - the bitmap is reallocated only when the frame size changes.
/// </summary>
[ToolboxItem(true)]
[Description("Displays raw video frames pushed from any thread.")]
public sealed class VideoView : Control
{
    private readonly object _gate = new();

    private Bitmap? _bitmap;
    /// <summary>
    /// 1 while a frame is converted or queued for the UI thread. Claimed with a
    /// compare-exchange, so even two producers at once (camera and screen
    /// overlap for a moment when switching) can never share the buffer below.
    /// </summary>
    private int _updatePending;

    /// <summary>
    /// Bumped by <see cref="Clear"/>. A frame captured under an older
    /// generation is discarded, so a frame already queued when the call ended
    /// cannot put the last picture back on screen.
    /// </summary>
    private int _generation;

    /// <summary>
    /// Conversion target, reused across frames. Safe because at most one frame
    /// is ever in flight: it is written only by the thread that claimed
    /// <see cref="_updatePending"/> and read only by the queued UI-thread update.
    /// </summary>
    private byte[]? _frameBuffer;
    private string _placeholderText = "No video";

    public VideoView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Color.Black;
        ForeColor = Color.FromArgb(154, 154, 168);
    }

    /// <summary>Text drawn in the middle of the control while there is no picture.</summary>
    [Category("Appearance")]
    [DefaultValue("No video")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>True once at least one frame has been drawn since the last <see cref="Clear"/>.</summary>
    [Browsable(false)]
    public bool HasFrame
    {
        get
        {
            lock (_gate)
            {
                return _bitmap is not null;
            }
        }
    }

    /// <summary>
    /// Pushes a frame. Safe to call from any thread and returns immediately.
    /// The caller's buffer is copied, so it may be reused after this returns.
    /// </summary>
    public void PushFrame(byte[] frame, int width, int height, int stride, VideoPixelFormatsEnum pixelFormat)
    {
        if (frame is null || width <= 0 || height <= 0)
        {
            return;
        }

        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        // Drop this frame if the previous one is still in flight.
        if (Interlocked.CompareExchange(ref _updatePending, 1, 0) != 0)
        {
            return;
        }

        var generation = Volatile.Read(ref _generation);
        var required = width * height * 3;

        if (_frameBuffer is null || _frameBuffer.Length < required)
        {
            _frameBuffer = new byte[required];
        }

        var bgr = _frameBuffer;

        try
        {
            // Copies into our own buffer, so the caller may reuse theirs.
            FrameConverter.ToBgr24(frame, width, height, stride, pixelFormat, bgr);
        }
        catch
        {
            Volatile.Write(ref _updatePending, 0);
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                try
                {
                    if (generation == Volatile.Read(ref _generation))
                    {
                        ApplyFrame(bgr, width, height);
                    }
                }
                finally
                {
                    Volatile.Write(ref _updatePending, 0);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed between the check above and BeginInvoke.
            // (Also covers ObjectDisposedException, which derives from it.)
            Volatile.Write(ref _updatePending, 0);
        }
    }

    /// <summary>Removes the picture so a finished call does not leave a frozen frame behind.</summary>
    public void Clear()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            Interlocked.Increment(ref _generation);

            try
            {
                BeginInvoke(new Action(Clear));
            }
            catch (InvalidOperationException) { }

            return;
        }

        Interlocked.Increment(ref _generation);

        lock (_gate)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }

        Invalidate();
    }

    /// <summary>
    /// If the handle goes away with an update queued, that update never runs;
    /// without this reset the view would stop accepting frames for good.
    /// </summary>
    protected override void OnHandleDestroyed(EventArgs e)
    {
        Volatile.Write(ref _updatePending, 0);
        base.OnHandleDestroyed(e);
    }

    private void ApplyFrame(byte[] bgr, int width, int height)
    {
        if (IsDisposed)
        {
            return;
        }

        lock (_gate)
        {
            if (_bitmap is null || _bitmap.Width != width || _bitmap.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            }

            // Format24bppRgb is stored B,G,R in memory - i.e. exactly our BGR24.
            // GDI+ pads each row to 4 bytes, so copy row by row using its stride.
            var data = _bitmap.LockBits(
                new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                var rowBytes = width * 3;

                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(bgr, y * rowBytes, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
                }
            }
            finally
            {
                _bitmap.UnlockBits(data);
            }
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(BackColor);

        lock (_gate)
        {
            if (_bitmap is not null)
            {
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                graphics.DrawImage(_bitmap, FitInside(_bitmap.Size, ClientRectangle));
                return;
            }
        }

        if (_placeholderText.Length > 0)
        {
            TextRenderer.DrawText(graphics, _placeholderText, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Largest rectangle with the frame's aspect ratio that fits, centred.</summary>
    private static Rectangle FitInside(Size frame, Rectangle bounds)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return bounds;
        }

        var scale = Math.Min(bounds.Width / (double)frame.Width, bounds.Height / (double)frame.Height);
        var width = (int)(frame.Width * scale);
        var height = (int)(frame.Height * scale);

        return new Rectangle(
            bounds.Left + ((bounds.Width - width) / 2),
            bounds.Top + ((bounds.Height - height) / 2),
            width,
            height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _bitmap?.Dispose();
                _bitmap = null;
            }
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Normalises whatever the codec or capture device produced into tightly
/// packed 24bpp BGR, written into <c>output</c> (at least width*height*3 bytes).
/// </summary>
internal static class FrameConverter
{
    public static void ToBgr24(byte[] source, int width, int height, int stride,
        VideoPixelFormatsEnum pixelFormat, byte[] output)
    {
        var outStride = width * 3;
        RequireLength(output, outStride * height);

        switch (pixelFormat)
        {
            case VideoPixelFormatsEnum.Bgr:
            {
                var sourceStride = stride > 0 ? stride : outStride;
                RequireLength(source, sourceStride * (height - 1) + outStride);

                for (var y = 0; y < height; y++)
                {
                    Buffer.BlockCopy(source, y * sourceStride, output, y * outStride, outStride);
                }

                return;
            }

            case VideoPixelFormatsEnum.Rgb:
            {
                var sourceStride = stride > 0 ? stride : outStride;
                RequireLength(source, sourceStride * (height - 1) + outStride);

                for (var y = 0; y < height; y++)
                {
                    var s = y * sourceStride;
                    var t = y * outStride;

                    for (var x = 0; x < width; x++, s += 3, t += 3)
                    {
                        output[t] = source[s + 2];
                        output[t + 1] = source[s + 1];
                        output[t + 2] = source[s];
                    }
                }

                return;
            }

            case VideoPixelFormatsEnum.Bgra:
            case VideoPixelFormatsEnum.Rgba:
            {
                var sourceStride = stride > 0 ? stride : width * 4;
                var isRgba = pixelFormat == VideoPixelFormatsEnum.Rgba;
                RequireLength(source, sourceStride * (height - 1) + (width * 4));

                for (var y = 0; y < height; y++)
                {
                    var s = y * sourceStride;
                    var t = y * outStride;

                    for (var x = 0; x < width; x++, s += 4, t += 3)
                    {
                        output[t] = isRgba ? source[s + 2] : source[s];
                        output[t + 1] = source[s + 1];
                        output[t + 2] = isRgba ? source[s] : source[s + 2];
                    }
                }

                return;
            }

            case VideoPixelFormatsEnum.I420:
            {
                // Planar: Y (w*h), then U and V at (w/2)*(h/2). Same layout
                // assumptions as MediaStack's crop, which produced this buffer.
                var ySize = width * height;
                var chromaWidth = width / 2;
                var chromaSize = chromaWidth * (height / 2);
                RequireLength(source, ySize + (2 * chromaSize));

                for (var y = 0; y < height; y++)
                {
                    var chromaRow = (y / 2) * chromaWidth;

                    for (var x = 0; x < width; x++)
                    {
                        var chromaIndex = chromaRow + (x / 2);
                        WriteBgr(output, (y * outStride) + (x * 3),
                            source[(y * width) + x],
                            source[ySize + chromaIndex],
                            source[ySize + chromaSize + chromaIndex]);
                    }
                }

                return;
            }

            case VideoPixelFormatsEnum.NV12:
            {
                // Planar Y, then one interleaved UV plane at half height.
                var ySize = width * height;
                RequireLength(source, ySize + (width * (height / 2)));

                for (var y = 0; y < height; y++)
                {
                    var uvRow = ySize + ((y / 2) * width);

                    for (var x = 0; x < width; x++)
                    {
                        var uv = uvRow + (x & ~1);
                        WriteBgr(output, (y * outStride) + (x * 3),
                            source[(y * width) + x],
                            source[uv],
                            source[uv + 1]);
                    }
                }

                return;
            }

            default:
                throw new NotSupportedException($"Pixel format {pixelFormat} is not handled.");
        }
    }

    /// <summary>BT.601 limited-range YUV to BGR, integer arithmetic.</summary>
    private static void WriteBgr(byte[] output, int offset, byte yValue, byte uValue, byte vValue)
    {
        var c = yValue - 16;
        var d = uValue - 128;
        var e = vValue - 128;

        output[offset] = Clamp(((298 * c) + (516 * d) + 128) >> 8);
        output[offset + 1] = Clamp(((298 * c) - (100 * d) - (208 * e) + 128) >> 8);
        output[offset + 2] = Clamp(((298 * c) + (409 * e) + 128) >> 8);
    }

    private static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

    private static void RequireLength(byte[] source, int required)
    {
        if (source.Length < required)
        {
            throw new ArgumentException("Frame buffer is smaller than its declared dimensions.");
        }
    }
}
