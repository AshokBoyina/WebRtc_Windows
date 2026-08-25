using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IntraTeams.Client.Media;

/// <summary>
/// Desktop capture built directly on GDI (BitBlt / StretchBlt / GetDIBits).
///
/// This exists because SIPSorceryMedia.Windows has no screen source at all -
/// WindowsVideoEndPoint is webcam-only. Rather than take a dependency on
/// SIPSorceryMedia.FFmpeg (which would force an FFmpeg shared-binary install
/// onto every desktop), we capture frames ourselves and push them into the VP8
/// encoder through IVideoSource.ExternalVideoSourceRawSample.
///
/// Output is tightly packed 24bpp BGR, top-down, which is exactly what
/// VideoPixelFormatsEnum.Bgr means to the encoder.
///
/// Known POC limitations, all called out in the README:
///   - primary monitor only (no monitor picker, no multi-monitor span)
///   - no mouse cursor in the captured image
///   - GDI cannot capture hardware-protected or some fullscreen exclusive apps
/// </summary>
public sealed class GdiScreenCapture : IDisposable
{
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const int HALFTONE = 4;
    private const int BI_RGB = 0;
    private const int DIB_RGB_COLORS = 0;

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private readonly int _targetWidth;
    private readonly int _fps;

    private Thread? _worker;
    private volatile bool _running;

    /// <summary>Raised on the capture thread. Buffer is reused, so copy it if you keep it.</summary>
    public event Action<byte[], int, int>? FrameCaptured;

    public GdiScreenCapture(int targetWidth, int fps)
    {
        // VP8 wants even dimensions for 4:2:0 chroma, and GetDIBits wants
        // 4-byte aligned rows. Rounding to a multiple of 16 satisfies both and
        // means the row stride is exactly width * 3 with no padding to strip.
        _targetWidth = Math.Max(160, targetWidth - (targetWidth % 16));
        _fps = Math.Clamp(fps, 1, 30);
    }

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _worker = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "ScreenCapture",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    public void Stop()
    {
        _running = false;

        // Give the loop one frame interval to exit on its own.
        _worker?.Join(TimeSpan.FromMilliseconds(1000 / _fps + 250));
        _worker = null;
    }

    private void CaptureLoop()
    {
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);

        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        // Preserve aspect ratio, then round the height to an even number.
        var width = Math.Min(_targetWidth, screenWidth - (screenWidth % 16));
        if (width <= 0)
        {
            width = _targetWidth;
        }

        var height = (int)Math.Round(screenHeight * (double)width / screenWidth);
        height -= height % 2;
        if (height <= 0)
        {
            height = 2;
        }

        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _fps);
        var buffer = new byte[width * height * 3];

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            // Negative height requests a top-down DIB. Without this the rows
            // come back bottom-up and the far end sees an upside-down desktop.
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 24,
            biCompression = BI_RGB
        };

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return;
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);

            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return;
            }

            previousBitmap = SelectObject(memoryDc, bitmap);

            // HALFTONE gives a far better downscale than the default
            // COLORONCOLOR, which simply drops rows and columns.
            SetStretchBltMode(memoryDc, HALFTONE);
            SetBrushOrgEx(memoryDc, 0, 0, IntPtr.Zero);

            var stopwatch = new Stopwatch();

            while (_running)
            {
                stopwatch.Restart();

                var copied = StretchBlt(
                    memoryDc, 0, 0, width, height,
                    screenDc, 0, 0, screenWidth, screenHeight,
                    SRCCOPY | CAPTUREBLT);

                if (copied)
                {
                    var scanLines = GetDIBits(
                        memoryDc, bitmap, 0, (uint)height, buffer, ref header, DIB_RGB_COLORS);

                    if (scanLines > 0)
                    {
                        FrameCaptured?.Invoke(buffer, width, height);
                    }
                }

                var elapsed = stopwatch.Elapsed;
                var remaining = frameInterval - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    Thread.Sleep(remaining);
                }
            }
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero)
            {
                SelectObject(memoryDc, previousBitmap);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------- interop

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool StretchBlt(
        IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc,
        int rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr lppt);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr hdc, IntPtr hbmp, uint startScan, uint scanLines,
        [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
}
