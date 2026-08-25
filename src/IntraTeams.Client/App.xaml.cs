using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using IntraTeams.Client.Infrastructure;
using IntraTeams.Client.ViewModels;
using SIPSorceryMedia.Windows;

namespace IntraTeams.Client;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A media or signaling fault on a background thread should surface as a
        // message, not silently kill the process.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ShowError("Unexpected error", ex);
            }
        };

        var config = AppConfig.Load(e.Args);

        if (config.ListCamerasAndExit)
        {
            await ShowCameraListAsync();
            Shutdown();
            return;
        }

        _viewModel = new MainViewModel(config);

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();

        await _viewModel.StartAsync();
    }

    private static async Task ShowCameraListAsync()
    {
        var text = new StringBuilder();

        try
        {
            // Note the spelling: the typo is in the SIPSorcery API itself.
            var devices = await WindowsVideoEndPoint.GetVideoCatpureDevices();

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
                    text.AppendLine($"{device.Name}");
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
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError("Unexpected error", e.Exception);
        e.Handled = true;
    }

    // Media faults repeat per frame. Without throttling, one bad capture format
    // produces a modal dialog twenty times a second and the app is unusable.
    private const int MaxErrorDialogs = 3;

    private static readonly HashSet<string> ReportedErrors = new(StringComparer.Ordinal);
    private static readonly object ErrorGate = new();
    private static int _errorDialogsShown;

    private static void ShowError(string title, Exception ex)
    {
        var message = $"{ex.Message}\n\n{ex.GetType().Name}";

        lock (ErrorGate)
        {
            // Same message twice: already told them.
            if (!ReportedErrors.Add(ex.Message))
            {
                Debug.WriteLine($"[suppressed duplicate] {message}");
                return;
            }

            if (_errorDialogsShown >= MaxErrorDialogs)
            {
                Debug.WriteLine($"[suppressed, dialog cap reached] {message}");
                return;
            }

            _errorDialogsShown++;

            if (_errorDialogsShown == MaxErrorDialogs)
            {
                message += "\n\nFurther errors will be written to the Connection log " +
                           "instead of shown here.";
            }
        }

        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.DisposeAsync();
        }

        base.OnExit(e);
    }
}
