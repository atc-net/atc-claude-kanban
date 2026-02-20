namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Discovers Claude Code sessions from ~/.claude/tasks/ and enriches them
/// with metadata from sessions-index.json, team configs, and subagent counts.
/// Uses <see cref="IMemoryCache"/> with 10-second TTL to avoid excessive disk reads.
/// </summary>
public sealed class SessionService
{
    private readonly ConcurrentDictionary<string, SessionInfo> sessionSnapshots = new(StringComparer.Ordinal);

    private readonly string claudeDir;
    private readonly IMemoryCache cache;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly SubagentService subagentService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="cache">Memory cache for session metadata.</param>
    /// <param name="jsonSerializerOptions">The shared JSON serializer options.</param>
    /// <param name="subagentService">The subagent service for enriching sessions with subagent counts.</param>
    public SessionService(
        string claudeDir,
        IMemoryCache cache,
        JsonSerializerOptions jsonSerializerOptions,
        SubagentService subagentService)
    {
        this.claudeDir = claudeDir;
        this.cache = cache;
        this.jsonSerializerOptions = jsonSerializerOptions;
        this.subagentService = subagentService;
    }

    /// <summary>
    /// Clears all in-memory session snapshots. Called on UI refresh so completed
    /// sessions don't persist across page reloads.
    /// </summary>
    public void ClearSnapshots()
        => sessionSnapshots.Clear();

    /// <summary>
    /// Returns sessions ordered by last modification time, up to <paramref name="limit"/>.
    /// </summary>
    /// <param name="limit">The maximum number of sessions to return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of sessions ordered by last modification time.</returns>
    public async Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"sessions:{limit}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<SessionInfo>? cached) && cached is not null)
        {
            return cached;
        }

        var sessions = await DiscoverSessionsAsync(cancellationToken);

#pragma warning disable AsyncFixer02 // In-memory LINQ on an already-awaited list
        var result = sessions
            .OrderByDescending(s => s.ModifiedAt)
            .Take(limit)
            .ToList();
#pragma warning restore AsyncFixer02

        cache.Set(cacheKey, (IReadOnlyList<SessionInfo>)result, TimeSpan.FromSeconds(10));
        return result;
    }

    /// <summary>
    /// Returns distinct project paths with their most recent modification times.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of distinct projects with their most recent modification times.</returns>
    public async Task<IReadOnlyList<ProjectInfo>> GetProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        const string cacheKey = "projects";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<ProjectInfo>? cached) && cached is not null)
        {
            return cached;
        }

        var projectPaths = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var sessions = await DiscoverSessionsAsync(cancellationToken);

        foreach (var session in sessions)
        {
            if (string.IsNullOrEmpty(session.Project))
            {
                continue;
            }

            if (!projectPaths.TryGetValue(session.Project, out var existing) || session.ModifiedAt > existing)
            {
                projectPaths[session.Project] = session.ModifiedAt;
            }
        }

#pragma warning disable AsyncFixer02 // In-memory LINQ on an already-awaited list
        var projects = projectPaths
            .Select(entry => new ProjectInfo { Path = entry.Key, ModifiedAt = entry.Value })
            .OrderByDescending(p => p.ModifiedAt)
            .ToList();
#pragma warning restore AsyncFixer02

        cache.Set(cacheKey, (IReadOnlyList<ProjectInfo>)projects, TimeSpan.FromSeconds(10));
        return projects;
    }

    private async Task<List<SessionInfo>> DiscoverSessionsAsync(
        CancellationToken cancellationToken)
    {
        var sessionIndexes = await LoadSessionIndexesAsync(cancellationToken);
        var sessions = new List<SessionInfo>();
        var discoveredIds = new HashSet<string>(StringComparer.Ordinal);

        // Discover sessions from tasks/ (primary source)
        var tasksDir = Path.Combine(claudeDir, "tasks");
        if (Directory.Exists(tasksDir))
        {
            foreach (var dir in Directory.GetDirectories(tasksDir))
            {
                var session = await BuildSessionInfoAsync(dir, sessionIndexes, cancellationToken);
                if (session is not null)
                {
                    sessions.Add(session);
                    discoveredIds.Add(session.Id);
                }
            }
        }

        // Discover sessions that have subagents but no tasks
        DiscoverSubagentOnlySessions(sessions, sessionIndexes, discoveredIds);

        // Merge lead sessions into their team sessions so subagents and metadata
        // appear on the team row instead of a separate subagent-only row
        await MergeLeadSessionsAsync(sessions, cancellationToken);

        // Snapshot active sessions and restore progress for sessions whose
        // task files were removed (either files only or entire directory).
        ApplyAndMergeSnapshots(sessions, discoveredIds);

        return sessions;
    }

    /// <summary>
    /// Snapshots sessions that have tasks, restores progress from snapshots
    /// for sessions whose task files were removed but whose directory still
    /// exists, and merges in snapshots for sessions whose entire task directory
    /// has been deleted.
    /// </summary>
    private void ApplyAndMergeSnapshots(
        List<SessionInfo> sessions,
        HashSet<string> discoveredIds)
    {
        foreach (var session in sessions)
        {
            if (session.TaskCount > 0)
            {
                sessionSnapshots[session.Id] = session;
            }
            else if (sessionSnapshots.TryGetValue(session.Id, out var snapshot) && snapshot.TaskCount > 0)
            {
                // Directory exists but all task files removed — restore progress from snapshot
                RestoreCompletedProgress(session, snapshot);
            }
        }

        // Merge in snapshots for sessions whose task dirs have been deleted entirely
        foreach (var (id, snapshot) in sessionSnapshots)
        {
            if (!discoveredIds.Contains(id))
            {
                RestoreCompletedProgress(snapshot, snapshot);
                sessions.Add(snapshot);
            }
        }
    }

    private static void RestoreCompletedProgress(
        SessionInfo target,
        SessionInfo source)
    {
        target.IsCompleted = true;
        target.Progress = 100;
        target.TaskCount = source.TaskCount;
        target.Completed = source.TaskCount;
        target.Pending = 0;
        target.InProgress = 0;
        target.PeakTaskCount = source.PeakTaskCount > 0
            ? source.PeakTaskCount
            : source.TaskCount;
    }

    private void DiscoverSubagentOnlySessions(
        List<SessionInfo> sessions,
        Dictionary<string, SessionIndex> sessionIndexes,
        HashSet<string> discoveredIds)
    {
        var projectsDir = Path.Combine(claudeDir, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return;
        }

        foreach (var hashDir in Directory.GetDirectories(projectsDir))
        {
            foreach (var sessionDir in Directory.GetDirectories(hashDir))
            {
                var sessionId = Path.GetFileName(sessionDir);
                if (discoveredIds.Contains(sessionId))
                {
                    continue;
                }

                var (subagentTotal, subagentActive) = subagentService.GetSubagentCounts(sessionId);
                if (subagentTotal == 0)
                {
                    continue;
                }

                var index = sessionIndexes.GetValueOrDefault(sessionId);
                sessions.Add(new SessionInfo
                {
                    Id = sessionId,
                    Name = index?.CustomTitle
                           ?? index?.Description
                           ?? ProjectDisplayName(index?.ProjectPath)
                           ?? ProjectDisplayName(index?.Cwd)
                           ?? sessionId,
                    Project = index?.ProjectPath ?? index?.Cwd,
                    Description = index?.Description,
                    GitBranch = index?.GitBranch,
                    HasPlan = PlanExistsForSession(index?.Slug ?? sessionId),
                    ModifiedAt = GetDirectoryLastWriteUtc(sessionDir),
                    CreatedAt = index?.CreatedAt,
                    Slug = index?.Slug ?? sessionId,
                    SubagentCount = subagentTotal,
                    ActiveSubagentCount = subagentActive,
                });
                discoveredIds.Add(sessionId);
            }
        }
    }

    /// <summary>
    /// Merges lead (parent) sessions into their team sessions so that subagent counts,
    /// project paths, and git branches appear on the team row in the sidebar.
    /// Lead sessions that have no tasks of their own are removed from the list.
    /// </summary>
    private async Task MergeLeadSessionsAsync(
        List<SessionInfo> sessions,
        CancellationToken cancellationToken)
    {
        var leadIdsToRemove = new HashSet<string>(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            if (!session.IsTeam)
            {
                continue;
            }

            var teamConfig = await TryLoadTeamConfigAsync(session.Id, cancellationToken);
            if (teamConfig?.LeadSessionId is null)
            {
                continue;
            }

            var lead = sessions.Find(s => s.Id == teamConfig.LeadSessionId);
            if (lead is null)
            {
                continue;
            }

            // Transfer subagent counts from lead to team session
            session.SubagentCount += lead.SubagentCount;
            session.ActiveSubagentCount += lead.ActiveSubagentCount;

            // Inherit metadata the team session may be missing.
            // Use IsNullOrEmpty to also catch empty strings from deserialization.
            if (string.IsNullOrEmpty(session.Project))
            {
                session.Project = lead.Project;
            }

            if (string.IsNullOrEmpty(session.GitBranch))
            {
                session.GitBranch = lead.GitBranch;
            }

            if (string.IsNullOrEmpty(session.Description))
            {
                session.Description = lead.Description;
            }

            // Remove the lead session if it has no tasks (subagent-only entry)
            if (lead.TaskCount == 0)
            {
                leadIdsToRemove.Add(lead.Id);
            }
        }

        if (leadIdsToRemove.Count > 0)
        {
            sessions.RemoveAll(s => leadIdsToRemove.Contains(s.Id));
        }
    }

    private static DateTime GetDirectoryLastWriteUtc(string directory)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(directory);
        }
        catch (IOException)
        {
            return DateTime.UtcNow;
        }
    }

    private async Task<SessionInfo?> BuildSessionInfoAsync(
        string sessionDir,
        Dictionary<string, SessionIndex> sessionIndexes,
        CancellationToken cancellationToken)
    {
        var sessionId = Path.GetFileName(sessionDir);
        var taskFiles = Directory.GetFiles(sessionDir, "*.json");

        var index = sessionIndexes.GetValueOrDefault(sessionId);
        var teamConfig = await TryLoadTeamConfigAsync(sessionId, cancellationToken);

        // For team sessions, look up the lead session's metadata
        SessionIndex? leadIndex = null;
        if (teamConfig?.LeadSessionId is not null)
        {
            sessionIndexes.TryGetValue(teamConfig.LeadSessionId, out leadIndex);
        }

        SessionInfo session;
        if (taskFiles.Length == 0)
        {
            var slug = index?.Slug ?? leadIndex?.Slug ?? sessionId;
            session = new SessionInfo
            {
                Id = sessionId,
                Name = ResolveSessionName(sessionId, index, teamConfig),
                Project = index?.ProjectPath ?? index?.Cwd
                          ?? leadIndex?.ProjectPath ?? leadIndex?.Cwd
                          ?? teamConfig?.WorkingDir,
                Description = index?.Description ?? leadIndex?.Description,
                GitBranch = index?.GitBranch ?? leadIndex?.GitBranch,
                HasPlan = PlanExistsForSession(slug),
                IsTeam = teamConfig is not null,
                MemberCount = teamConfig?.Members?.Count ?? 0,
                ModifiedAt = GetDirectoryLastWriteUtc(sessionDir),
                CreatedAt = index?.CreatedAt,
                Slug = slug,
            };
        }
        else
        {
            session = await BuildActiveSessionAsync(
                sessionId,
                taskFiles,
                index,
                leadIndex,
                teamConfig,
                cancellationToken);
        }

        // Enrich with subagent counts
        var (subagentTotal, subagentActive) = subagentService.GetSubagentCounts(sessionId);
        session.SubagentCount = subagentTotal;
        session.ActiveSubagentCount = subagentActive;

        return session;
    }

    private async Task<SessionInfo> BuildActiveSessionAsync(
        string sessionId,
        string[] taskFiles,
        SessionIndex? index,
        SessionIndex? leadIndex,
        TeamConfig? teamConfig,
        CancellationToken cancellationToken)
    {
        var (tasks, latestModified) = await ReadTaskFilesAsync(taskFiles, cancellationToken);

        var project = index?.ProjectPath
                      ?? index?.Cwd
                      ?? leadIndex?.ProjectPath
                      ?? leadIndex?.Cwd
                      ?? teamConfig?.WorkingDir;

        var slug = index?.Slug ?? leadIndex?.Slug ?? sessionId;

        var session = new SessionInfo
        {
            Id = sessionId,
            Name = ResolveSessionName(sessionId, index, teamConfig),
            Project = project,
            Description = index?.Description ?? leadIndex?.Description,
            GitBranch = index?.GitBranch ?? leadIndex?.GitBranch,
            TaskCount = tasks.Count,
            Pending = tasks.Count(t => t.Status == "pending"),
            InProgress = tasks.Count(t => t.Status == "in_progress"),
            Completed = tasks.Count(t => t.Status == "completed"),
            HasPlan = PlanExistsForSession(slug),
            IsTeam = teamConfig is not null,
            MemberCount = teamConfig?.Members?.Count ?? 0,
            ModifiedAt = latestModified == DateTime.MinValue ? DateTime.UtcNow : latestModified,
            CreatedAt = index?.CreatedAt,
            Slug = slug,
        };

        session.Progress = session.TaskCount > 0
            ? (int)System.Math.Round(100.0 * session.Completed / session.TaskCount)
            : 0;

        return session;
    }

    private static string ResolveSessionName(
        string sessionId,
        SessionIndex? index,
        TeamConfig? teamConfig)
        => teamConfig?.Name ??
           index?.CustomTitle ??
           index?.Description ??
           ProjectDisplayName(index?.ProjectPath) ??
           ProjectDisplayName(index?.Cwd) ??
           sessionId;

    private async Task<(List<ClaudeTask> Tasks, DateTime LatestModified)> ReadTaskFilesAsync(
        string[] taskFiles,
        CancellationToken cancellationToken)
    {
        var tasks = new List<ClaudeTask>();
        var latestModified = DateTime.MinValue;

        foreach (var file in taskFiles)
        {
            try
            {
                // Read modification time BEFORE content (TOCTOU fix: conservative direction)
                var fileModified = File.GetLastWriteTimeUtc(file);

                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var task = JsonSerializer.Deserialize<ClaudeTask>(json, jsonSerializerOptions);
                if (task is not null && !task.IsInternal)
                {
                    tasks.Add(task);
                }

                if (fileModified > latestModified)
                {
                    latestModified = fileModified;
                }
            }
            catch (JsonException)
            {
                // Skip malformed task files
            }
            catch (IOException)
            {
                // Skip locked files
            }
        }

        return (tasks, latestModified);
    }

    private async Task<Dictionary<string, SessionIndex>> LoadSessionIndexesAsync(
        CancellationToken cancellationToken)
    {
        var indexes = new Dictionary<string, SessionIndex>(StringComparer.Ordinal);
        var projectsDir = Path.Combine(claudeDir, "projects");

        if (!Directory.Exists(projectsDir))
        {
            return indexes;
        }

        foreach (var hashDir in Directory.GetDirectories(projectsDir))
        {
            var originalPath = await ReadOriginalPathAsync(hashDir, cancellationToken);

            await LoadIndexEntriesAsync(hashDir, indexes, cancellationToken);
            await DiscoverJsonlSessionsAsync(hashDir, originalPath, indexes, cancellationToken);
        }

        return indexes;
    }

    private async Task LoadIndexEntriesAsync(
        string hashDir,
        Dictionary<string, SessionIndex> indexes,
        CancellationToken cancellationToken)
    {
        var indexFile = Path.Combine(hashDir, "sessions-index.json");
        if (!File.Exists(indexFile))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(indexFile, cancellationToken);
            var parsed = JsonSerializer.Deserialize<SessionIndexFile>(json, jsonSerializerOptions);
            if (parsed?.Entries is null)
            {
                return;
            }

            foreach (var entry in parsed.Entries.Where(entry => entry.Id is not null))
            {
                indexes[entry.Id!] = entry;
            }
        }
        catch (JsonException)
        {
            // Skip malformed index files
        }
        catch (IOException)
        {
            // Skip locked files
        }
    }

    private async Task DiscoverJsonlSessionsAsync(
        string hashDir,
        string? projectPath,
        Dictionary<string, SessionIndex> indexes,
        CancellationToken cancellationToken)
    {
        foreach (var jsonlFile in Directory.GetFiles(hashDir, "*.jsonl"))
        {
            var fileSessionId = Path.GetFileNameWithoutExtension(jsonlFile);
            var metadata = await TryReadJsonlMetadataAsync(jsonlFile, cancellationToken);

            // Child JSONL files reference the parent sessionId and carry the slug.
            // Propagate the slug to the parent session's index entry.
            if (metadata.ParentSessionId is not null &&
                metadata.Slug is not null &&
                indexes.TryGetValue(metadata.ParentSessionId, out var parentIndex) &&
                parentIndex.Slug is null)
            {
                parentIndex.Slug = metadata.Slug;
            }

            if (indexes.TryGetValue(fileSessionId, out var existing))
            {
                // Merge fields from JSONL into existing index entry (sessions-index.json lacks slug/customTitle)
                existing.Slug ??= metadata.Slug;
                existing.CustomTitle ??= metadata.CustomTitle;
                existing.ProjectPath ??= projectPath;
                existing.Cwd ??= metadata.Cwd;
                continue;
            }

            indexes[fileSessionId] = new SessionIndex
            {
                Id = fileSessionId,
                ProjectPath = projectPath ?? metadata.Cwd,
                Cwd = metadata.Cwd,
                GitBranch = metadata.GitBranch,
                Slug = metadata.Slug,
                CustomTitle = metadata.CustomTitle,
            };
        }
    }

    /// <summary>
    /// Scans the beginning of a session JSONL file to extract metadata.
    /// Scans up to 250 lines because hook/progress entries can push the
    /// first user message (which carries the slug) deep into the file.
    /// </summary>
    private static async Task<JsonlMetadata> TryReadJsonlMetadataAsync(
        string jsonlFile,
        CancellationToken cancellationToken)
    {
        string? cwd = null;
        string? gitBranch = null;
        string? slug = null;
        string? parentSessionId = null;
        string? customTitle = null;

        try
        {
            using var reader = new StreamReader(jsonlFile);
            for (var i = 0; i < 250; i++)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                ExtractJsonlFields(line, ref cwd, ref gitBranch, ref slug, ref parentSessionId, ref customTitle);

                if (cwd is not null && slug is not null && customTitle is not null)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            // Skip on error — metadata is best-effort
        }

        return new JsonlMetadata
        {
            Cwd = cwd,
            GitBranch = gitBranch,
            Slug = slug,
            ParentSessionId = parentSessionId,
            CustomTitle = customTitle,
        };
    }

    /// <summary>
    /// Extracts known metadata fields from a single JSONL line, filling in
    /// only those values that are still null.
    /// </summary>
    private static void ExtractJsonlFields(
        string line,
        ref string? cwd,
        ref string? gitBranch,
        ref string? slug,
        ref string? parentSessionId,
        ref string? customTitle)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (cwd is null && root.TryGetProperty("cwd", out var cwdEl))
            {
                cwd = cwdEl.GetString();
            }

            if (gitBranch is null && root.TryGetProperty("gitBranch", out var branchEl))
            {
                gitBranch = branchEl.GetString();
            }

            if (slug is null && root.TryGetProperty("slug", out var slugEl))
            {
                slug = slugEl.GetString();
            }

            if (parentSessionId is null && root.TryGetProperty("sessionId", out var sidEl))
            {
                parentSessionId = sidEl.GetString();
            }

            if (customTitle is null &&
                root.TryGetProperty("type", out var typeEl) &&
                typeEl.GetString() == "custom-title" &&
                root.TryGetProperty("customTitle", out var ctEl))
            {
                customTitle = ctEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Skip malformed lines
        }
    }

    private async Task<string?> ReadOriginalPathAsync(
        string hashDir,
        CancellationToken cancellationToken)
    {
        var indexFile = Path.Combine(hashDir, "sessions-index.json");
        if (!File.Exists(indexFile))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(indexFile, cancellationToken);
            var parsed = JsonSerializer.Deserialize<SessionIndexFile>(json, jsonSerializerOptions);
            return parsed?.OriginalPath;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task<TeamConfig?> TryLoadTeamConfigAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var configFile = Path.Combine(claudeDir, "teams", sessionId, "config.json");
        if (!File.Exists(configFile))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configFile, cancellationToken);
            return JsonSerializer.Deserialize<TeamConfig>(json, jsonSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? ProjectDisplayName(string? projectPath)
        => string.IsNullOrEmpty(projectPath)
            ? null
            : Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private bool PlanExistsForSession(string? slug)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return false;
        }

        var plansDir = Path.Combine(claudeDir, "plans");
        if (!Directory.Exists(plansDir))
        {
            return false;
        }

        // Exact match by slug (plan files are always named after slugs)
        var slugFile = Path.Combine(plansDir, $"{slug}.md");
        if (File.Exists(slugFile))
        {
            return true;
        }

        // Prefix match for agent-specific plan variants (e.g., "my-slug-agent-1.md")
        return Directory.GetFiles(plansDir, $"{slug}*.md").Length > 0;
    }
}