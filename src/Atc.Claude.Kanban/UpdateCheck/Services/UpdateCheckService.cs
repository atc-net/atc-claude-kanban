namespace Atc.Claude.Kanban.UpdateCheck.Services;

/// <summary>
/// Background service that checks NuGet for newer versions on startup and optionally
/// performs an automatic update via <c>dotnet tool update</c>.
/// </summary>
public sealed partial class UpdateCheckService : BackgroundService
{
    private const string PackageId = "atc-claude-kanban";
    private const string CacheDirectoryName = "atc-claude-kanban";
    private const string CacheFileName = "update-check.json";
    private const string Dim = "\e[90m";
    private const string BrightWhite = "\e[97m";
    private const string Cyan = "\e[36m";
    private const string Reset = "\e[0m";

    [SuppressMessage("Design", "S1075:URIs should not be hardcoded", Justification = "NuGet API endpoint is stable")]
    private static readonly Uri NuGetIndexUri = new("https://api.nuget.org/v3-flatcontainer/atc-claude-kanban/index.json");

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient httpClient;
    private readonly SseClientManager sseClientManager;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly ILogger<UpdateCheckService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateCheckService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for NuGet API calls.</param>
    /// <param name="sseClientManager">The SSE client manager for broadcasting updates.</param>
    /// <param name="jsonSerializerOptions">The shared JSON serializer options.</param>
    /// <param name="logger">The logger instance.</param>
    public UpdateCheckService(
        HttpClient httpClient,
        SseClientManager sseClientManager,
        JsonSerializerOptions jsonSerializerOptions,
        ILogger<UpdateCheckService> logger)
    {
        this.httpClient = httpClient;
        this.sseClientManager = sseClientManager;
        this.jsonSerializerOptions = jsonSerializerOptions;
        this.logger = logger;
    }

    /// <summary>
    /// Executes the version check and optional auto-update on startup.
    /// Completes after the check finishes (does not run for the lifetime of the app).
    /// </summary>
    /// <param name="stoppingToken">The cancellation token for application shutdown.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Update check must never crash the application")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Small delay to let the startup banner and watcher messages print first
            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            await CheckForUpdateAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — expected
        }
        catch (Exception ex)
        {
            // Never let update check crash the application
            LogUpdateCheckFailed(ex);
        }
    }

    private async Task CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        LogUpdateCheckStarted();

        var currentVersion = GetCurrentVersion();
        if (currentVersion is null)
        {
            return;
        }

        var cacheFilePath = GetCacheFilePath();
        var cachedResult = await ReadCacheAsync(cacheFilePath, cancellationToken);

        if (cachedResult is not null &&
            (DateTimeOffset.UtcNow - cachedResult.LastCheck) < CacheTtl)
        {
            HandleCacheHit(cachedResult, currentVersion);
            return;
        }

        var latestVersion = await FetchLatestVersionAsync(cancellationToken);
        if (latestVersion is null ||
            latestVersion <= currentVersion)
        {
            await WriteCacheAsync(
                cacheFilePath,
                currentVersion.ToString(3),
                updatePerformed: false,
                cancellationToken);

            return;
        }

        await PerformUpdateAsync(
            cacheFilePath,
            currentVersion.ToString(3),
            latestVersion.ToString(3),
            cancellationToken);
    }

    private static Version? GetCurrentVersion()
    {
        var informational = typeof(UpdateCheckService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0];

        return informational is not null &&
               Version.TryParse(informational, out var version)
            ? version
            : null;
    }

    private static string GetCacheFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CacheDirectoryName,
            CacheFileName);

    private async Task<UpdateCheckCache?> ReadCacheAsync(
        string cacheFilePath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cacheFilePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(cacheFilePath, cancellationToken);
            return JsonSerializer.Deserialize<UpdateCheckCache>(json, jsonSerializerOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private void HandleCacheHit(
        UpdateCheckCache cachedResult,
        Version currentVersion)
    {
        LogUpdateCheckCacheHit(cachedResult.LastCheck);

        if (cachedResult.UpdatePerformed ||
            !Version.TryParse(cachedResult.LatestVersion, out var cachedLatest) ||
            cachedLatest <= currentVersion)
        {
            return;
        }

        // Re-show update notice from cache (no NuGet call needed)
        PrintUpdateAvailable(
            currentVersion.ToString(3),
            cachedResult.LatestVersion);

        BroadcastVersionUpdate(
            currentVersion.ToString(3),
            cachedResult.LatestVersion);
    }

    private async Task<Version?> FetchLatestVersionAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(HttpTimeout);

            var json = await httpClient.GetStringAsync(NuGetIndexUri, timeoutCts.Token);
            var index = JsonSerializer.Deserialize<NuGetVersionIndex>(json, jsonSerializerOptions);

            return index?.Versions
                .Where(v => !v.Contains('-', StringComparison.Ordinal))
                .Select(x => Version.TryParse(x, out var parsed) ? parsed : null)
                .Where(x => x is not null)
                .Max();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LogUpdateCheckFailed(ex);
            return null;
        }
    }

    private async Task PerformUpdateAsync(
        string cacheFilePath,
        string currentVersionString,
        string latestVersionString,
        CancellationToken cancellationToken)
    {
        LogUpdateAvailable(currentVersionString, latestVersionString);

        var updateSucceeded = await TryAutoUpdateAsync(cancellationToken);
        if (updateSucceeded)
        {
            LogUpdateSucceeded(latestVersionString);
            PrintUpdateSuccess(latestVersionString);
        }
        else
        {
            PrintUpdateAvailable(currentVersionString, latestVersionString);
        }

        await WriteCacheAsync(
            cacheFilePath,
            latestVersionString,
            updateSucceeded,
            cancellationToken);

        BroadcastVersionUpdate(currentVersionString, latestVersionString);
    }

    private async Task<bool> TryAutoUpdateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"tool update -g {PackageId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            process.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(UpdateTimeout);

            await process.WaitForExitAsync(timeoutCts.Token);

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            LogUpdateFailed(ex);
            return false;
        }
    }

    private static void PrintUpdateSuccess(string latestVersion)
    {
        System.Console.WriteLine();
        System.Console.WriteLine($"  {Cyan}\u2139{Reset}  {BrightWhite}Update successful!{Reset} {Dim}v{latestVersion} will be used on your next run.{Reset}");
        System.Console.WriteLine();
    }

    private static void PrintUpdateAvailable(
        string currentVersion,
        string latestVersion)
    {
        System.Console.WriteLine();
        System.Console.WriteLine($"  {Cyan}\u2139{Reset}  {Dim}Update available:{Reset} {BrightWhite}{currentVersion}{Reset} {Dim}\u2192{Reset} {BrightWhite}{latestVersion}{Reset}");
        System.Console.WriteLine($"     {Dim}Run:{Reset} {BrightWhite}dotnet tool update -g {PackageId}{Reset}");
        System.Console.WriteLine();
    }

    private async Task WriteCacheAsync(
        string cacheFilePath,
        string latestVersion,
        bool updatePerformed,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(cacheFilePath);
            if (directory is not null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var cache = new UpdateCheckCache
            {
                LastCheck = DateTimeOffset.UtcNow,
                LatestVersion = latestVersion,
                UpdatePerformed = updatePerformed,
            };

            var json = JsonSerializer.Serialize(cache, jsonSerializerOptions);
            await File.WriteAllTextAsync(cacheFilePath, json, cancellationToken);
        }
        catch (IOException)
        {
            // Best-effort — cache write failure is not critical
        }
    }

    private void BroadcastVersionUpdate(
        string currentVersion,
        string latestVersion)
        => sseClientManager.BroadcastNotification(new SseNotification
        {
            Type = "version-update",
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
        });
}