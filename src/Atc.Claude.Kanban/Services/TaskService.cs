namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Reads and writes individual Claude Code task JSON files.
/// Supports CRUD operations and dependency-safe deletion.
/// </summary>
public sealed class TaskService
{
    private static readonly HashSet<string> AllowedUpdateFields = new(StringComparer.Ordinal)
    {
        "subject",
        "description",
        "status",
        "owner",
        "activeForm",
        "blocks",
        "blockedBy",
    };

    private readonly ConcurrentDictionary<string, IReadOnlyList<ClaudeTask>> taskSnapshots = new(StringComparer.Ordinal);

    private readonly string claudeDir;
    private readonly SessionService sessionService;
    private readonly JsonSerializerOptions jsonSerializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="sessionService">The session service for resolving session metadata.</param>
    /// <param name="jsonSerializerOptions">The shared JSON serializer options.</param>
    public TaskService(
        string claudeDir,
        SessionService sessionService,
        JsonSerializerOptions jsonSerializerOptions)
    {
        this.claudeDir = claudeDir;
        this.sessionService = sessionService;
        this.jsonSerializerOptions = jsonSerializerOptions;
    }

    /// <summary>
    /// Clears all in-memory task snapshots. Called on UI refresh so completed
    /// session tasks don't persist across page reloads.
    /// </summary>
    public void ClearSnapshots()
        => taskSnapshots.Clear();

    /// <summary>
    /// Returns all tasks for a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of tasks for the given session.</returns>
    public async Task<IReadOnlyList<ClaudeTask>> GetTasksForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionDir = GetValidatedSessionDir(sessionId);
        if (sessionDir is null || !Directory.Exists(sessionDir))
        {
            // Task files deleted — return snapshot if available
            return taskSnapshots.GetValueOrDefault(sessionId) ?? [];
        }

        var tasks = new List<ClaudeTask>();

        foreach (var file in Directory.GetFiles(sessionDir, "*.json"))
        {
            try
            {
                var createdAt = File.GetCreationTimeUtc(file);
                var updatedAt = File.GetLastWriteTimeUtc(file);

                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var task = JsonSerializer.Deserialize<ClaudeTask>(json, jsonSerializerOptions);
                if (task is not null && !task.IsInternal)
                {
                    task.CreatedAt = createdAt;
                    task.UpdatedAt = updatedAt;
                    tasks.Add(task);
                }
            }
            catch (JsonException)
            {
                // Skip malformed files
            }
            catch (IOException)
            {
                // Skip locked files
            }
        }

        // Sort by numeric ID, matching original Node.js behavior
        tasks.Sort((a, b) =>
        {
            var aNum = int.TryParse(a.Id, out var av) ? av : int.MaxValue;
            var bNum = int.TryParse(b.Id, out var bv) ? bv : int.MaxValue;
            return aNum.CompareTo(bNum);
        });

        if (tasks.Count > 0)
        {
            // Snapshot current tasks so they survive file deletion
            taskSnapshots[sessionId] = tasks;
        }
        else if (taskSnapshots.TryGetValue(sessionId, out var snapshot))
        {
            // Directory exists but all files removed — return snapshot
            return snapshot;
        }

        return tasks;
    }

    /// <summary>
    /// Returns internal agent lifecycle tasks for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of internal agent tasks.</returns>
    public async Task<IReadOnlyList<ClaudeTask>> GetAgentTasksForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionDir = GetValidatedSessionDir(sessionId);
        if (sessionDir is null || !Directory.Exists(sessionDir))
        {
            return [];
        }

        var tasks = new List<ClaudeTask>();

        foreach (var file in Directory.GetFiles(sessionDir, "*.json"))
        {
            try
            {
                var createdAt = File.GetCreationTimeUtc(file);
                var updatedAt = File.GetLastWriteTimeUtc(file);

                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var task = JsonSerializer.Deserialize<ClaudeTask>(json, jsonSerializerOptions);
                if (task is not null && task.IsInternal)
                {
                    task.CreatedAt = createdAt;
                    task.UpdatedAt = updatedAt;
                    tasks.Add(task);
                }
            }
            catch (JsonException)
            {
                // Skip malformed files
            }
            catch (IOException)
            {
                // Skip locked files
            }
        }

        return tasks;
    }

    /// <summary>
    /// Returns all tasks across every session, enriched with session metadata.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of all tasks enriched with session metadata.</returns>
    public async Task<IReadOnlyList<ClaudeTask>> GetAllTasksAsync(
        CancellationToken cancellationToken = default)
    {
        var tasksDir = Path.Combine(claudeDir, "tasks");
        if (!Directory.Exists(tasksDir))
        {
            return [];
        }

        var allTasks = new List<ClaudeTask>();
        var sessions = await sessionService.GetSessionsAsync(int.MaxValue, cancellationToken);
        var sessionLookup = sessions.ToDictionary(s => s.Id, StringComparer.Ordinal);

        foreach (var dir in Directory.GetDirectories(tasksDir))
        {
            var sessionId = Path.GetFileName(dir);
            sessionLookup.TryGetValue(sessionId, out var sessionInfo);
            var tasks = await GetTasksForSessionAsync(sessionId, cancellationToken);

            foreach (var task in tasks)
            {
                task.SessionId = sessionId;
                task.SessionName = sessionInfo?.Name ?? sessionId;
                task.Project = sessionInfo?.Project;
            }

            allTasks.AddRange(tasks);
        }

        return allTasks;
    }

    /// <summary>
    /// Merges the given key-value updates into a task's JSON file.
    /// Only fields in <see cref="AllowedUpdateFields"/> are written; others are silently ignored.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="updates">The key-value pairs to merge into the task.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The updated task on success, or <see langword="null"/> on failure.</returns>
    public async Task<ClaudeTask?> UpdateTaskAsync(
        string sessionId,
        string taskId,
        IDictionary<string, JsonElement> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var taskFile = await FindTaskFileAsync(sessionId, taskId, cancellationToken);
        if (taskFile is null)
        {
            return null;
        }

        try
        {
            var timestampBeforeRead = File.GetLastWriteTimeUtc(taskFile);

            var json = await File.ReadAllTextAsync(taskFile, cancellationToken);
            var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, jsonSerializerOptions);
            if (document is null)
            {
                return null;
            }

            foreach (var update in updates.Where(u => AllowedUpdateFields.Contains(u.Key)))
            {
                document[update.Key] = update.Value;
            }

            // Optimistic concurrency: if the file was modified after we read it,
            // another process (e.g. Claude Code) wrote to it and we should not overwrite.
            var timestampBeforeWrite = File.GetLastWriteTimeUtc(taskFile);
            if (timestampBeforeWrite != timestampBeforeRead)
            {
                return null;
            }

            var updatedJson = JsonSerializer.Serialize(document, jsonSerializerOptions);
            await File.WriteAllTextAsync(taskFile, updatedJson, cancellationToken);
            return JsonSerializer.Deserialize<ClaudeTask>(updatedJson, jsonSerializerOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Appends a user note to a task's description.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="note">The note text to append.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The updated task on success, or <see langword="null"/> on failure.</returns>
    public async Task<ClaudeTask?> AddNoteAsync(
        string sessionId,
        string taskId,
        string note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);

        var taskFile = await FindTaskFileAsync(sessionId, taskId, cancellationToken);
        if (taskFile is null)
        {
            return null;
        }

        try
        {
            var timestampBeforeRead = File.GetLastWriteTimeUtc(taskFile);

            var json = await File.ReadAllTextAsync(taskFile, cancellationToken);
            var task = JsonSerializer.Deserialize<ClaudeTask>(json, jsonSerializerOptions);
            if (task is null)
            {
                return null;
            }

            task.Description = $"{task.Description}\n\n---\n\n#### [Note added by user]\n\n{note.Trim()}";

            var timestampBeforeWrite = File.GetLastWriteTimeUtc(taskFile);
            if (timestampBeforeWrite != timestampBeforeRead)
            {
                return null;
            }

            var updatedJson = JsonSerializer.Serialize(task, jsonSerializerOptions);
            await File.WriteAllTextAsync(taskFile, updatedJson, cancellationToken);
            return task;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes a task file after validating that no other tasks depend on it.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A tuple indicating success, an optional error message, and blocked task IDs.</returns>
    public async Task<(bool Success, string? Error, IReadOnlyList<string>? BlockedTasks)> DeleteTaskAsync(
        string sessionId,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var taskFile = await FindTaskFileAsync(sessionId, taskId, cancellationToken);
        if (taskFile is null)
        {
            return (false, "Task not found", null);
        }

        // Check for tasks that depend on this one
        var allTasks = await GetTasksForSessionAsync(sessionId, cancellationToken);
#pragma warning disable AsyncFixer02 // In-memory LINQ on an already-awaited list
        var dependents = allTasks
            .Where(t => t.BlockedBy is not null &&
                        t.BlockedBy.Contains(taskId, StringComparer.Ordinal))
            .ToList();
#pragma warning restore AsyncFixer02

        if (dependents.Count > 0)
        {
#pragma warning disable AsyncFixer02 // In-memory LINQ on an already-awaited list
            var blockedIds = dependents.Select(t => t.Id).ToList();
#pragma warning restore AsyncFixer02
            return (false, "Cannot delete task that blocks other tasks", blockedIds);
        }

        try
        {
            File.Delete(taskFile);
            return (true, null, null);
        }
        catch (IOException ex)
        {
            return (false, ex.Message, null);
        }
    }

    private async Task<string?> FindTaskFileAsync(
        string sessionId,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var sessionDir = GetValidatedSessionDir(sessionId);
        if (sessionDir is null || !Directory.Exists(sessionDir))
        {
            return null;
        }

        // Task files are named by ID -- try common patterns
        var candidates = new[]
        {
            Path.Combine(sessionDir, $"{taskId}.json"),
            Path.Combine(sessionDir, $"task-{taskId}.json"),
        };

        var directMatch = candidates
            .Where(f => PathHelper.IsUnderDirectory(Path.GetFullPath(f), sessionDir))
            .FirstOrDefault(File.Exists);

        if (directMatch is not null)
        {
            return directMatch;
        }

        // Fallback: scan directory for matching task ID inside the file
        foreach (var file in Directory.GetFiles(sessionDir, "*.json"))
        {
            if (await MatchesTaskIdAsync(file, taskId, cancellationToken))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the validated session directory path, or null if the session ID
    /// would result in a path traversal outside the tasks directory.
    /// </summary>
    private string? GetValidatedSessionDir(string sessionId)
    {
        var tasksDir = Path.GetFullPath(Path.Combine(claudeDir, "tasks"));
        var sessionDir = Path.GetFullPath(Path.Combine(tasksDir, sessionId));

        return PathHelper.IsUnderDirectory(sessionDir, tasksDir) ? sessionDir : null;
    }

    private async Task<bool> MatchesTaskIdAsync(
        string filePath,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var task = JsonSerializer.Deserialize<ClaudeTask>(json, jsonSerializerOptions);
            return task?.Id == taskId;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}