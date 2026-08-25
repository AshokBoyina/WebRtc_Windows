using System.IO;
using System.Text.Json;

namespace IntraTeams.Client.Infrastructure;

/// <summary>
/// Configuration read straight from appsettings.json with System.Text.Json.
/// Deliberately avoids Microsoft.Extensions.Configuration - one small file of
/// settings does not justify the dependency in a POC.
/// </summary>
public sealed class AppConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5000";

    public string CameraDeviceId { get; set; } = "";
    public int CameraWidth { get; set; } = 640;
    public int CameraHeight { get; set; } = 480;
    public int CameraFps { get; set; } = 20;

    public int ScreenShareWidth { get; set; } = 1280;
    public int ScreenShareFps { get; set; } = 8;

    /// <summary>
    /// Appended to the Windows username in the people list. Lets you run two
    /// instances on one machine during a demo and still tell them apart.
    /// Set with --suffix on the command line.
    /// </summary>
    public string DisplayNameSuffix { get; set; } = "";

    /// <summary>Set by --list-cameras; makes the app print devices and exit.</summary>
    public bool ListCamerasAndExit { get; set; }

    public static AppConfig Load(string[] args)
    {
        var config = new AppConfig();

        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<AppConfig>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                if (loaded is not null)
                {
                    config = loaded;
                }
            }
            catch (JsonException)
            {
                // Fall through to defaults rather than refusing to start.
            }
        }

        // Command line overrides, e.g.
        //   IntraTeams.exe --server http://10.0.0.5:5000 --suffix "window 2"
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--server" when i + 1 < args.Length:
                    config.ServerUrl = args[++i];
                    break;
                case "--suffix" when i + 1 < args.Length:
                    config.DisplayNameSuffix = args[++i];
                    break;
                case "--camera" when i + 1 < args.Length:
                    config.CameraDeviceId = args[++i];
                    break;
                case "--list-cameras":
                    config.ListCamerasAndExit = true;
                    break;
            }
        }

        return config;
    }
}
