// BUILD-TIME VERIFICATION ONLY. Not part of the shipped application.
//
// Stand-in declarations for the handful of WPF types the view model and the
// video renderer touch, so those files can be compiled on Linux. Signatures
// mirror the real WPF API; behaviour is deliberately inert.

namespace System.Windows.Threading
{
    public class Dispatcher
    {
        public bool CheckAccess() => true;

        public object? BeginInvoke(Delegate method, params object[] args)
        {
            method.DynamicInvoke(args.Length == 0 ? null : args);
            return null;
        }
    }
}

namespace System.Windows
{
    using System.Windows.Threading;

    public enum Visibility { Visible = 0, Hidden = 1, Collapsed = 2 }

    public enum HorizontalAlignment { Left = 0, Center = 1, Right = 2, Stretch = 3 }

    public struct Int32Rect
    {
        public Int32Rect(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class Application
    {
        public static Application? Current { get; } = new();
        public Dispatcher Dispatcher { get; } = new();
        public void Shutdown() { }
    }
}

namespace System.Windows.Media
{
    public class ImageSource { }

    public struct PixelFormat
    {
        public PixelFormat(string name) => Name = name;
        public string Name { get; }
    }

    public static class PixelFormats
    {
        public static PixelFormat Bgr24 => new("Bgr24");
        public static PixelFormat Bgra32 => new("Bgra32");
    }
}

namespace System.Windows.Media.Imaging
{
    using System.Windows;
    using System.Windows.Media;

    public class WriteableBitmap : ImageSource
    {
        public WriteableBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY,
            PixelFormat pixelFormat, object? palette)
        {
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
        }

        public int PixelWidth { get; }
        public int PixelHeight { get; }

        public void WritePixels(Int32Rect sourceRect, Array sourceBuffer, int stride, int offset) { }
    }
}

namespace System.Windows.Data
{
    using System.Globalization;

    public interface IValueConverter
    {
        object Convert(object value, Type targetType, object parameter, CultureInfo culture);
        object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture);
    }
}
