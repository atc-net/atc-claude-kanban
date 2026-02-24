namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Background service that watches ~/.claude/ subdirectories for file changes
/// and broadcasts <see cref="SseNotification"/> events via <see cref="SseClientManager"/>.
/// Uses <see cref="FileSystemWatcher"/> with debouncing and a bounded <see cref="Channel{T}"/>.
/// </summary>
public sealed partial class ClaudeDirectoryWatcher : BackgroundService
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DebounceCleanupInterval = TimeSpan.FromSeconds(30);

    private readonly string claudeDir;
    private readonly SseClientManager sseClientManager;
    private readonly IMemoryCache cache;
    private readonly ILogger<ClaudeDirectoryWatcher> logger;
    private readonly Channel<FileChangeEvent> channel;
    private readonly ConcurrentDictionary<string, DateTime> debounceTracker = new(StringComparer.Ordinal);
    private readonly List<FileSystemWatcher> watchers = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeDirectoryWatcher"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="sseClientManager">The SSE client manager to broadcast file change notifications to.</param>
    /// <param name="cache">Memory cache to invalidate on file system changes.</param>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    public ClaudeDirectoryWatcher(
        string claudeDir,
        SseClientManager sseClientManager,
        IMemoryCache cache,
        ILogger<ClaudeDirectoryWatcher> logger)
    {
        this.claudeDir = claudeDir;
        this.sseClientManager = sseClientManager;
        this.cache = cache;
        this.logger = logger;

        channel = Channel.CreateBounded<FileChangeEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    /// <summary>
    /// Creates file system watchers and begins processing change events.
    /// </summary>
    /// <param name="stoppingToken">Token triggered when the host is shutting down.</param>
    /// <returns>A task that completes when the service stops.</returns>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CreateWatchers();

        return ProcessEventsAsync(stoppingToken);
    }

    /// <summary>
    /// Stops all file system watchers and completes the event channel.
    /// </summary>
    /// <param name="cancellationToken">Token triggered when the host requests stop.</param>
    /// <returns>A task that completes when the service has stopped.</returns>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        watchers.Clear();
        channel.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a <see cref="FileSystemWatcher"/> for each Claude subdirectory
    /// (tasks, teams, projects, plans) and wires up change event handlers.
    /// </summary>
    private void CreateWatchers()
    {
        string[] subDirectories = ["tasks", "teams", "projects", "plans"];

        foreach (var subDirectory in subDirectories)
        {
            var dir = Path.Combine(claudeDir, subDirectory);
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (IOException ex)
                {
                    LogCouldNotCreateDirectory(ex, dir);
                    continue;
                }
            }

            try
            {
                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 65536,
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.Size,
                };

                watcher.Changed += (_, e) => OnFileEvent(e, subDirectory);
                watcher.Created += (_, e) => OnFileEvent(e, subDirectory);
                watcher.Deleted += (_, e) => OnFileEvent(e, subDirectory);
                watcher.Renamed += (_, e) => OnFileEvent(e, subDirectory);
                watcher.Error += (_, e) => LogWatcherError(e.GetException(), subDirectory);

                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);

                LogWatching(dir);
                PrintWatcherRegistered(dir);
            }
            catch (IOException ex)
            {
                LogWatcherCreateFailed(ex, dir);
            }
        }
    }

    /// <summary>
    /// Prints a styled confirmation line to the console when a watcher is registered.
    /// </summary>
    /// <param name="directory">The directory being watched.</param>
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "Styled console output is not localizable")]
    private static void PrintWatcherRegistered(string directory)
    {
        var displayDir = PathHelper.CollapseHomePath(directory);
        System.Console.WriteLine($"  \e[32m✓\e[0m \e[90mWatching\e[0m  \e[97m{displayDir}\e[0m");
    }

    /// <summary>
    /// Handles a file system event by debouncing rapid changes and writing to the event channel.
    /// </summary>
    /// <param name="e">The file system event arguments.</param>
    /// <param name="category">The subdirectory category (tasks, teams, projects, or plans).</param>
    private void OnFileEvent(
        FileSystemEventArgs e,
        string category)
    {
        // Filter by file extension per category (match original Node.js behavior)
        var ext = Path.GetExtension(e.FullPath);
        var relevant = category switch
        {
            "tasks" or "teams" => string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase),
            "projects" => string.Equals(ext, ".jsonl", StringComparison.OrdinalIgnoreCase),
            "plans" => string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

        if (!relevant)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var key = e.FullPath;

        if (debounceTracker.TryGetValue(key, out var lastEvent) &&
            (now - lastEvent) < DebounceWindow)
        {
            return;
        }

        debounceTracker[key] = now;

        var changeEvent = new FileChangeEvent
        {
            FullPath = e.FullPath,
            Category = category,
            EventType = MapChangeType(e.ChangeType),
            FileName = Path.GetFileName(e.FullPath),
        };

        channel.Writer.TryWrite(changeEvent);
    }

    /// <summary>
    /// Reads file change events from the channel and broadcasts SSE notifications.
    /// Periodically cleans up stale entries from the debounce tracker.
    /// </summary>
    /// <param name="stoppingToken">Token triggered when the host is shutting down.</param>
    private async Task ProcessEventsAsync(CancellationToken stoppingToken)
    {
        var lastCleanup = DateTime.UtcNow;

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var notification = CategorizeEvent(evt);
                    sseClientManager.BroadcastNotification(notification);

                    // For plan content changes, also broadcast a metadata-update
                    // so the session list refreshes hasPlan and planTitle fields
                    if (evt.Category is "plans" && notification.Type is "plan-update")
                    {
                        sseClientManager.BroadcastNotification(
                            new SseNotification { Type = "metadata-update" });
                    }

                    if (notification.Type is "metadata-update" || evt.Category is "plans")
                    {
                        InvalidateSessionCache(notification.SessionId);
                    }

                    if (notification.Type is "team-update" && notification.TeamName is not null)
                    {
                        cache.Remove($"team:{notification.TeamName}");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    LogProcessingError(ex);
                }

                var now = DateTime.UtcNow;
                if (now - lastCleanup > DebounceCleanupInterval)
                {
                    CleanupDebounceTracker(now);
                    lastCleanup = now;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — expected
        }
    }

    /// <summary>
    /// Removes stale entries from the debounce tracker to prevent unbounded memory growth.
    /// </summary>
    private void CleanupDebounceTracker(DateTime now)
    {
        foreach (var entry in debounceTracker)
        {
            if (now - entry.Value > DebounceCleanupInterval)
            {
                debounceTracker.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>
    /// Clears cached session, project, and subagent data so the next API call reads fresh data from disk.
    /// Mirrors the original's <c>lastMetadataRefresh = 0</c> approach.
    /// </summary>
    /// <param name="sessionId">Optional session identifier to also invalidate subagent cache for.</param>
    private void InvalidateSessionCache(string? sessionId)
    {
        cache.Remove("projects");
        for (var i = 1; i <= 100; i++)
        {
            cache.Remove($"sessions:{i}");
        }

        if (sessionId is not null)
        {
            cache.Remove($"subagents:{sessionId}");
        }
    }

    /// <summary>
    /// Converts a <see cref="FileChangeEvent"/> into a typed <see cref="SseNotification"/>
    /// based on the file category.
    /// </summary>
    /// <param name="evt">The file change event to categorize.</param>
    /// <returns>An SSE notification with the appropriate type and identifiers.</returns>
    private static SseNotification CategorizeEvent(FileChangeEvent evt)
    {
        var sessionId = ExtractSessionId(evt.FullPath, evt.Category);

        return evt.Category switch
        {
            "tasks" => new SseNotification
            {
                Type = "update",
                SessionId = sessionId,
                Event = evt.EventType,
                File = evt.FileName,
            },
            "teams" => new SseNotification { Type = "team-update", TeamName = sessionId },
            "projects" => new SseNotification { Type = "metadata-update", SessionId = sessionId },
            "plans" when evt.EventType is "change" => new SseNotification
            {
                Type = "plan-update",
                Slug = Path.GetFileNameWithoutExtension(evt.FullPath),
            },
            "plans" => new SseNotification { Type = "metadata-update" },
            _ => new SseNotification { Type = "update", SessionId = sessionId },
        };
    }

    /// <summary>
    /// Maps a <see cref="WatcherChangeTypes"/> value to the event name used by the original
    /// Node.js implementation (add, change, unlink).
    /// </summary>
    private static string MapChangeType(WatcherChangeTypes changeType)
        => changeType switch
        {
            WatcherChangeTypes.Created => "add",
            WatcherChangeTypes.Deleted => "unlink",
            _ => "change",
        };

    /// <summary>
    /// Extracts the session or team identifier from the full file path.
    /// For most categories, this is the directory immediately after the category folder.
    /// For "projects", the structure is projects/{hash}/{sessionId}/... so we skip the hash.
    /// </summary>
    /// <param name="fullPath">The absolute path of the changed file.</param>
    /// <param name="category">The category subdirectory name.</param>
    /// <returns>The extracted identifier, or <see langword="null"/> if not found.</returns>
    private static string? ExtractSessionId(
        string fullPath,
        string category)
    {
        var parts = fullPath.Replace('\\', '/').Split('/');
        var categoryIndex = Array.IndexOf(parts, category);

        // projects/{hash}/{sessionId}/... — skip the hash directory
        var offset = string.Equals(category, "projects", StringComparison.Ordinal) ? 2 : 1;

        return categoryIndex >= 0 && categoryIndex + offset < parts.Length
            ? parts[categoryIndex + offset]
            : null;
    }
}