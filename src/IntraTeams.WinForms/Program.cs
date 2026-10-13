using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using IntraTeams.Client.Infrastructure;
using SIPSorceryMedia.Windows;

namespace IntraTeams.WinForms;

internal static class Program
{
    /// <summary>
    /// Command line is the same as the WPF client:
    ///   --server http://10.0.0.5:5000   override ServerUrl
    ///   --suffix A                      tell two instances on one machine apart
    ///   --camera &lt;device id&gt;          pick a webcam
    ///   --list-cameras                  show camera ids and exit
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // Applies the ApplicationHighDpiMode / visual styles / default font
        // settings declared in the .csproj.
        ApplicationConfiguration.Initialize();

        // A media or signaling fault on a background thread should surface as a
        // message, not silently kill the process.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorReporter.Show(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                ErrorReporter.Show(ex);
            }
        };

        var config = AppConfig.Load(args);

        if (config.ListCamerasAndExit)
        {
            ShowCameraList();
            return;
        }

        Application.Run(new MainForm(config));
    }

    private static void ShowCameraList()
    {
        var text = new StringBuilder();

        try
        {
            // Run off the STA thread; Media Foundation enumeration is happy on
            // the pool. Note the spelling: the typo is in the SIPSorcery API.
            var devices = Task.Run(() => WindowsVideoEndPoint.GetVideoCatpureDevices())
                .GetAwaiter().GetResult();

            if (devices is null || devices.Count == 0)
            {
                text.AppendLine("No video capture devices were found.");
            }
            else
            {
                text.AppendLine("Set CameraDeviceId in appsettings.json to one of these ids:");
                text.AppendLine();

                foreach (var device in devices)
                {
                    text.AppendLine(device.Name);
                    text.AppendLine($"    {device.ID}");
                    text.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            text.AppendLine($"Could not enumerate cameras: {ex.Message}");
        }

        MessageBox.Show(text.ToString(), "IntraTeams cameras",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

/// <summary>
/// Shows unexpected errors, throttled. Media faults tend to repeat per frame;
/// without a cap one bad capture format means a modal dialog twenty times a
/// second and an unusable app.
/// </summary>
internal static class ErrorReporter
{
    private const int MaxDialogs = 3;

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static int _shown;

    public static void Show(Exception ex)
    {
        var message = $"{ex.Message}\n\n{ex.GetType().Name}";

        lock (Gate)
        {
            if (!Reported.Add(ex.Message) || _shown >= MaxDialogs)
            {
                Debug.WriteLine($"[suppressed] {message}");
                return;
            }

            _shown++;

            if (_shown == MaxDialogs)
            {
                message += "\n\nFurther errors will not be shown as dialogs.";
            }
        }

        MessageBox.Show(message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
