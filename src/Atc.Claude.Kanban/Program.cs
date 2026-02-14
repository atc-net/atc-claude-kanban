namespace Atc.Claude.Kanban;

/// <summary>
/// Entry point — parses CLI arguments, wires up DI, and launches the Kestrel web server.
/// </summary>
public static partial class Program
{
    private const int DefaultPort = 3456;

    public static void Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var (port, openBrowser, claudeDir) = ParseArguments(args);

        var builder = WebApplication.CreateSlimBuilder(args);
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Set a short shutdown timeout so Ctrl+C doesn't hang waiting for SSE connections
        builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(3));

        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Atc.Claude.Kanban", LogLevel.Information);

        builder.Services.AddKanbanServices(claudeDir);
        builder.Services.AddEndpointDefinitions(typeof(Program));

        var app = builder.Build();

        app.UseEndpointDefinitions();
        app.UseEmbeddedStaticFiles();

        var url = $"http://localhost:{port}";
        LogDashboardStarting(app.Logger, url);
        LogWatchingDirectory(app.Logger, claudeDir);

        if (openBrowser)
        {
            TryOpenBrowser(url);
        }

        app.Run();
    }

    [LoggerMessage(
        EventId = LoggingEventIdConstants.DashboardStarting,
        Level = LogLevel.Information,
        Message = "ATC Claude Kanban dashboard starting at {Url}.")]
    private static partial void LogDashboardStarting(
        ILogger logger,
        string url);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.WatchingDirectory,
        Level = LogLevel.Information,
        Message = "Watching Claude directory: {ClaudeDir}.")]
    private static partial void LogWatchingDirectory(
        ILogger logger,
        string claudeDir);

    private static (int Port, bool OpenBrowser, string ClaudeDir) ParseArguments(
        string[] args)
    {
        var port = DefaultPort;
        var openBrowser = false;
        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude");

        var i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out var parsedPort))
                    {
                        port = parsedPort;
                    }

                    i += 2;
                    break;

                case "--open":
                    openBrowser = true;
                    i++;
                    break;

                case "--dir" when i + 1 < args.Length:
                    claudeDir = args[i + 1];
                    i += 2;
                    break;

                default:
                    i++;
                    break;
            }
        }

        return (port, openBrowser, claudeDir);
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (InvalidOperationException)
        {
            // Best-effort — browser open may fail in headless environments
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Best-effort — browser open may fail on some platforms
        }
    }
}