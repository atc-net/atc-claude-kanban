namespace Atc.Claude.Kanban;

/// <summary>
/// Entry point — parses CLI arguments, wires up DI, and launches the Kestrel web server.
/// </summary>
public static class Program
{
    private const int DefaultPort = 3456;
    private const int MaxPortAttempts = 10;

    public static void Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        System.Console.OutputEncoding = System.Text.Encoding.UTF8;

        var (port, explicitPort, openBrowser, claudeDir) = ParseArguments(args);

        for (var attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            try
            {
                RunServer(port, openBrowser, claudeDir, args);
                return;
            }
            catch (IOException ex) when (!explicitPort && attempt < MaxPortAttempts - 1 && IsAddressInUse(ex))
            {
                System.Console.WriteLine($"Port {port} is in use, trying {port + 1}...");
                port++;
            }
        }
    }

    private static void RunServer(
        int port,
        bool openBrowser,
        string claudeDir,
        string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Set a short shutdown timeout so Ctrl+C doesn't hang waiting for SSE connections
        builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(3));

        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Atc.Claude.Kanban", LogLevel.Information);

        // Suppress verbose stack traces from the host when port binding fails (auto-port handles this)
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.Critical);

        builder.Services.AddKanbanServices(claudeDir);
        builder.Services.AddEndpointDefinitions(typeof(Program));

        var app = builder.Build();

        app.UseEndpointDefinitions();
        app.UseEmbeddedStaticFiles();

        var url = $"http://localhost:{port}";

        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";

        StartupBanner.Print(url, claudeDir, version);

        if (openBrowser)
        {
            TryOpenBrowser(url);
        }

        app.Run();
    }

    private static (int Port, bool ExplicitPort, bool OpenBrowser, string ClaudeDir) ParseArguments(
        string[] args)
    {
        var port = DefaultPort;
        var explicitPort = false;
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
                        explicitPort = true;
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

        return (port, explicitPort, openBrowser, claudeDir);
    }

    private static bool IsAddressInUse(Exception ex)
    {
        // Kestrel wraps SocketException in IOException
        if (ex is IOException ioEx && ioEx.InnerException is System.Net.Sockets.SocketException socketEx)
        {
            return socketEx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse;
        }

        // Walk inner exceptions in case of deeper wrapping
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is System.Net.Sockets.SocketException se &&
                se.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                return true;
            }

            inner = inner.InnerException;
        }

        return false;
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