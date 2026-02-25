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

        var options = ParseArguments(args);

        if (options.ExplicitPort)
        {
            EnsurePortAvailable(options.Port);
        }
        else
        {
            options = options with { Port = FindAvailablePort(options.Port) };
        }

        RunServer(options, args);
    }

    private static CliOptions ParseArguments(string[] args)
    {
        var port = DefaultPort;
        var explicitPort = false;
        var openBrowser = false;
        var noUpdateCheck = false;

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

                case "--no-update-check":
                    noUpdateCheck = true;
                    i++;
                    break;

                default:
                    i++;
                    break;
            }
        }

        return new CliOptions
        {
            Port = port,
            ExplicitPort = explicitPort,
            OpenBrowser = openBrowser,
            ClaudeDir = claudeDir,
            NoUpdateCheck = noUpdateCheck,
        };
    }

    private static void EnsurePortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(
                System.Net.IPAddress.Loopback,
                port);

            listener.Start();
            listener.Stop();
        }
        catch (SocketException)
        {
            System.Console.WriteLine();
            System.Console.WriteLine($"  \e[31m\u2717\e[0m  \e[97mPort {port}\e[0m \e[90mis already in use.\e[0m");
            System.Console.WriteLine($"     \e[90mRelease the port or omit\e[0m \e[97m--port\e[0m \e[90mto auto-select.\e[0m");
            System.Console.WriteLine();
            Environment.Exit(1);
        }
    }

    private static int FindAvailablePort(int startPort)
    {
        for (var attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            var candidatePort = startPort + attempt;

            try
            {
                using var listener = new TcpListener(
                    System.Net.IPAddress.Loopback,
                    candidatePort);

                listener.Start();
                listener.Stop();

                return candidatePort;
            }
            catch (SocketException)
            {
                System.Console.WriteLine($"Port {candidatePort} is in use, trying {candidatePort + 1}...");
            }
        }

        // All attempts exhausted — return the last tried port and let Kestrel fail with a clear error
        return startPort + MaxPortAttempts - 1;
    }

    private static void RunServer(
        CliOptions options,
        string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        builder.WebHost.UseUrls($"http://localhost:{options.Port}");

        // Set a short shutdown timeout so Ctrl+C doesn't hang waiting for SSE connections
        builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(3));

        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Atc.Claude.Kanban", LogLevel.Information);

        // Suppress verbose stack traces from the host when port binding fails (auto-port handles this)
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.Critical);

        builder.Services.AddKanbanServices(options.ClaudeDir);
        builder.Services.AddEndpointDefinitions(typeof(Program));

        if (!options.NoUpdateCheck)
        {
            builder.Services.AddUpdateCheckService();
        }

        var app = builder.Build();

        app.UseEndpointDefinitions();
        app.UseEmbeddedStaticFiles();

        var url = $"http://localhost:{options.Port}";

        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";

        StartupBanner.Print(url, options.ClaudeDir, version);

        if (options.OpenBrowser)
        {
            TryOpenBrowser(url);
        }

        app.Run();
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (InvalidOperationException)
        {
            // Best-effort — browser open may fail in headless environments
        }
        catch (Win32Exception)
        {
            // Best-effort — browser open may fail on some platforms
        }
    }
}