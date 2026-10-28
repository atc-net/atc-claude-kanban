namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Reads when each task of a session went in progress and when it left it, from the task tool
/// results recorded in the session's transcript. A transcript is scanned once; later calls read
/// only the bytes appended since, so refreshing a live session's timeline stays cheap.
/// </summary>
public sealed class TaskTimelineService
{
    private const int ReadChunkSize = 256 * 1024;

    private readonly ConcurrentDictionary<string, TranscriptScan> scans = new(StringComparer.Ordinal);
    private readonly string claudeDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskTimelineService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    public TaskTimelineService(string claudeDir)
    {
        this.claudeDir = claudeDir;
    }

    // Only lines carrying one of these can change a task's history. Every other line is skipped
    // before parsing, which keeps the large tool-output lines from costing anything.
    private static ReadOnlySpan<byte> StatusChangeMarker
        => "\"statusChange\""u8;

    private static ReadOnlySpan<byte> TaskCreatedMarker
        => "\"toolUseResult\":{\"task\":"u8;

    /// <summary>
    /// Returns the recorded status history of every task in a session, or null when the
    /// session has no transcript on disk.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The task timeline, or null when the session has no transcript.</returns>
    public async Task<TaskTimelineResponse?> GetTaskTimelineAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var jsonlPath = FindSessionJsonlPath(sessionId);
        if (jsonlPath is null)
        {
            return null;
        }

        var scan = scans.GetOrAdd(jsonlPath, _ => new TranscriptScan());
        await scan.Gate.WaitAsync(cancellationToken);
        try
        {
            await AdvanceAsync(jsonlPath, scan, cancellationToken);
        }
        catch (IOException)
        {
            // Keep what was read before; the next call picks up from the same offset
        }
        finally
        {
            scan.Gate.Release();
        }

        return new TaskTimelineResponse(sessionId, scan.Snapshot);
    }

    /// <summary>
    /// Builds the task timeline from a whole transcript. The incremental scan and this share
    /// the same line handling.
    /// </summary>
    /// <param name="content">The transcript's JSONL content.</param>
    /// <returns>The recorded history of each task, ordered by task id.</returns>
    internal static IReadOnlyList<TaskTimelineEntry> BuildTimeline(
        string content)
    {
        var histories = new Dictionary<string, TaskHistory>(StringComparer.Ordinal);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content + "\n");
        ApplyCompleteLines(bytes, histories);
        return ToEntries(histories);
    }

    private static async Task AdvanceAsync(
        string jsonlPath,
        TranscriptScan scan,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            jsonlPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var length = stream.Length;
        if (length < scan.Offset)
        {
            // The transcript was rewritten rather than appended to, so start over
            scan.Histories.Clear();
            scan.Offset = 0;
            scan.Snapshot = [];
        }

        if (length == scan.Offset)
        {
            return;
        }

        stream.Seek(scan.Offset, SeekOrigin.Begin);
        var buffer = new byte[(int)System.Math.Min(ReadChunkSize, length - scan.Offset)];
        var filled = 0;

        while (true)
        {
            if (filled == buffer.Length)
            {
                // A single line longer than the buffer (a large tool output) needs more room
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken);
            if (read == 0)
            {
                break;
            }

            filled += read;

            // The text after the last newline may be a line still being written; it is kept
            // in the buffer and, if the file ends there, read again by the next call
            var consumed = ApplyCompleteLines(buffer.AsSpan(0, filled), scan.Histories);
            if (consumed > 0)
            {
                Buffer.BlockCopy(buffer, consumed, buffer, 0, filled - consumed);
                filled -= consumed;
                scan.Offset += consumed;
            }
        }

        scan.Snapshot = ToEntries(scan.Histories);
    }

    /// <summary>
    /// Applies every newline-terminated line in <paramref name="region"/> and returns the
    /// number of bytes consumed, which ends just after the last newline.
    /// </summary>
    private static int ApplyCompleteLines(
        ReadOnlySpan<byte> region,
        Dictionary<string, TaskHistory> histories)
    {
        var consumed = 0;
        while (true)
        {
            var newline = region[consumed..].IndexOf((byte)'\n');
            if (newline < 0)
            {
                return consumed;
            }

            ApplyLine(region.Slice(consumed, newline), histories);
            consumed += newline + 1;
        }
    }

    private static void ApplyLine(
        ReadOnlySpan<byte> line,
        Dictionary<string, TaskHistory> histories)
    {
        if (line.IsEmpty || line[0] != (byte)'{')
        {
            return;
        }

        if (line.IndexOf(StatusChangeMarker) < 0 && line.IndexOf(TaskCreatedMarker) < 0)
        {
            return;
        }

        try
        {
            var reader = new Utf8JsonReader(line);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (!root.TryGetProperty("toolUseResult", out var result) ||
                result.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("timestamp", out var timestampElement) ||
                !timestampElement.TryGetDateTime(out var timestamp))
            {
                return;
            }

            timestamp = timestamp.ToUniversalTime();

            if (result.TryGetProperty("task", out var task))
            {
                ApplyCreate(task, timestamp, histories);
            }
            else if (result.TryGetProperty("statusChange", out var statusChange))
            {
                ApplyStatusChange(result, statusChange, timestamp, histories);
            }
        }
        catch (JsonException)
        {
            // A malformed line carries no evidence either way
        }
    }

    /// <summary>
    /// A TaskCreate result starts the task's history afresh. Task ids are reused once a list is
    /// emptied and refilled, and a resumed session's new list numbers from 1 again, so whatever
    /// was recorded under the id before belongs to an earlier task.
    /// </summary>
    private static void ApplyCreate(
        JsonElement task,
        DateTime timestamp,
        Dictionary<string, TaskHistory> histories)
    {
        // A create result carries only id and subject; a task that has a status is a read
        if (task.ValueKind != JsonValueKind.Object ||
            task.TryGetProperty("status", out _) ||
            !task.TryGetProperty("id", out var idElement) ||
            idElement.GetString() is not { } taskId)
        {
            return;
        }

        histories[taskId] = new TaskHistory(timestamp);
    }

    private static void ApplyStatusChange(
        JsonElement result,
        JsonElement statusChange,
        DateTime timestamp,
        Dictionary<string, TaskHistory> histories)
    {
        if (!result.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.True ||
            !result.TryGetProperty("taskId", out var idElement) ||
            idElement.GetString() is not { } taskId ||
            statusChange.ValueKind != JsonValueKind.Object ||
            !statusChange.TryGetProperty("to", out var toElement) ||
            toElement.GetString() is not { } to)
        {
            return;
        }

        if (string.Equals(to, "deleted", StringComparison.Ordinal))
        {
            histories.Remove(taskId);
            return;
        }

        if (!histories.TryGetValue(taskId, out var history))
        {
            // Created before the transcript begins, or by another session
            history = new TaskHistory(createdAt: null);
            histories[taskId] = history;
        }

        history.Apply(to, timestamp);
    }

    private static TaskTimelineEntry[] ToEntries(
        Dictionary<string, TaskHistory> histories)
        => histories
            .OrderBy(pair => int.TryParse(pair.Key, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new TaskTimelineEntry(pair.Key, pair.Value.CreatedAt, pair.Value.Segments.ToArray()))
            .ToArray();

    private string? FindSessionJsonlPath(string sessionId)
    {
        var projectsDir = Path.Combine(claudeDir, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return null;
        }

        foreach (var hashDir in Directory.GetDirectories(projectsDir))
        {
            var jsonlFile = Path.Combine(hashDir, $"{sessionId}.jsonl");
            if (File.Exists(jsonlFile))
            {
                return jsonlFile;
            }
        }

        return null;
    }

    private sealed class TaskHistory
    {
        public TaskHistory(DateTime? createdAt)
        {
            CreatedAt = createdAt;
        }

        public DateTime? CreatedAt { get; }

        public List<TaskTimelineSegment> Segments { get; } = [];

        /// <summary>
        /// Entering in_progress opens a segment and leaving it closes the open one, so a task that
        /// is reopened gets one segment per run. A task completed without a recorded start gets a
        /// zero-length segment, which still marks when it finished.
        /// </summary>
        public void Apply(
            string status,
            DateTime timestamp)
        {
            var isOpen = Segments.Count > 0 && Segments[^1].End is null;
            if (string.Equals(status, "in_progress", StringComparison.Ordinal))
            {
                if (!isOpen)
                {
                    Segments.Add(new TaskTimelineSegment(timestamp, End: null));
                }
            }
            else if (isOpen)
            {
                Segments[^1] = Segments[^1] with { End = timestamp };
            }
            else if (string.Equals(status, "completed", StringComparison.Ordinal))
            {
                Segments.Add(new TaskTimelineSegment(timestamp, timestamp));
            }
        }
    }

    private sealed class TranscriptScan
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public Dictionary<string, TaskHistory> Histories { get; } = new(StringComparer.Ordinal);

        public long Offset { get; set; }

        public IReadOnlyList<TaskTimelineEntry> Snapshot { get; set; } = [];
    }
}