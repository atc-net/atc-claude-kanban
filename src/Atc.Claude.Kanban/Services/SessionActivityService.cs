namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Derives session activity status (thinking/waiting/idle/error) and token usage
/// from JSONL transcript tail reads. Uses a lightweight 4KB read from the end of the file.
/// Cached with 5-second TTL and file modification time validation.
/// </summary>
public sealed class SessionActivityService
{
    private const int TailReadSize = 32768;
    private const double ThinkingThresholdSeconds = 15;
    private const double IdleThresholdSeconds = 60;

    private readonly string claudeDir;
    private readonly IMemoryCache cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionActivityService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="cache">Memory cache for activity status.</param>
    public SessionActivityService(
        string claudeDir,
        IMemoryCache cache)
    {
        this.claudeDir = claudeDir;
        this.cache = cache;
    }

    /// <summary>
    /// Derives the activity status of a session from the tail of its JSONL transcript.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The activity status string: "thinking", "waiting", "error", or "idle".</returns>
    public async Task<string> GetActivityStatusAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"activity:{sessionId}";
        var jsonlPath = FindSessionJsonlPath(sessionId);
        if (jsonlPath is null)
        {
            return "idle";
        }

        DateTime lastModifiedUtc;
        try
        {
            lastModifiedUtc = File.GetLastWriteTimeUtc(jsonlPath);
        }
        catch (IOException)
        {
            return "idle";
        }

        if (cache.TryGetValue(cacheKey, out CachedActivity? cached) &&
            cached is not null &&
            cached.LastModifiedUtc == lastModifiedUtc)
        {
            return cached.Status;
        }

        var status = await DeriveStatusFromTailAsync(jsonlPath, lastModifiedUtc, cancellationToken);
        cache.Set(cacheKey, new CachedActivity(status, lastModifiedUtc), TimeSpan.FromSeconds(5));
        return status;
    }

    /// <summary>
    /// Accumulates token usage from all usage blocks in a session's JSONL transcript.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Token usage summary, or null if no transcript found.</returns>
    public async Task<SessionTokenUsage?> GetTokenUsageAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var jsonlPath = FindSessionJsonlPath(sessionId);
        return jsonlPath is null
            ? null
            : await GetTokenUsageForPathAsync(jsonlPath, cancellationToken);
    }

    /// <summary>
    /// Accumulates token usage from an explicit transcript path (used for subagent
    /// transcripts), cached by file modification time.
    /// </summary>
    /// <param name="jsonlPath">Absolute path to the JSONL transcript.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The accumulated token usage (empty when the file cannot be read).</returns>
    public async Task<SessionTokenUsage> GetTokenUsageForPathAsync(
        string jsonlPath,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"tokens-path:{jsonlPath}";

        DateTime lastModifiedUtc;
        try
        {
            lastModifiedUtc = File.GetLastWriteTimeUtc(jsonlPath);
        }
        catch (IOException)
        {
            return new SessionTokenUsage();
        }

        if (cache.TryGetValue(cacheKey, out CachedTokenUsage? cached) &&
            cached is not null &&
            cached.LastModifiedUtc == lastModifiedUtc)
        {
            return cached.Usage;
        }

        var usage = await AccumulateTokenUsageAsync(jsonlPath, cancellationToken);
        cache.Set(cacheKey, new CachedTokenUsage(usage, lastModifiedUtc), TimeSpan.FromSeconds(10));
        return usage;
    }

    private static async Task<string> DeriveStatusFromTailAsync(
        string filePath,
        DateTime lastModifiedUtc,
        CancellationToken cancellationToken)
    {
        var elapsed = DateTime.UtcNow - lastModifiedUtc;

        // Always read the tail — tool_use waiting uses conversation timestamps, not file mtime
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var fileLength = stream.Length;
            if (fileLength == 0)
            {
                return "idle";
            }

            var readSize = (int)System.Math.Min(TailReadSize, fileLength);
            stream.Seek(fileLength - readSize, SeekOrigin.Begin);

            var buffer = new byte[readSize];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken);
            var tail = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

            return DeriveStatusFromEntries(tail, elapsed.TotalSeconds);
        }
        catch (IOException)
        {
            return "idle";
        }
    }

    /// <summary>
    /// Derives status from the last few JSONL entries using elapsed time and content types.
    /// Follows the Stargx deriveStatus pattern: only classify within 15s window, idle otherwise.
    /// </summary>
    /// <param name="tail">The raw tail content from the JSONL file.</param>
    /// <param name="elapsedSeconds">Seconds since the file was last modified.</param>
    /// <returns>Activity status string.</returns>
    internal static string DeriveStatusFromEntries(
        string tail,
        double elapsedSeconds)
    {
        var scanResult = ScanLastEntries(tail);

        if (scanResult.HasError)
        {
            return "error";
        }

        // tool_use with no user response = waiting for permission.
        // Uses the conversation entry's own timestamp (not file mtime) because hooks keep touching the file.
        if (string.Equals(scanResult.LastConversationEntryType, "assistant", StringComparison.Ordinal) &&
            scanResult.LastConversationContentTypes.Contains("tool_use", StringComparer.Ordinal))
        {
            var conversationElapsed = GetConversationElapsed(scanResult.LastConversationTimestamp);
            if (conversationElapsed >= ThinkingThresholdSeconds)
            {
                return "waiting";
            }
        }

        if (elapsedSeconds > IdleThresholdSeconds)
        {
            return "idle";
        }

        // Within 15s window — classify based on most recent entry of any type
        if (elapsedSeconds < ThinkingThresholdSeconds)
        {
            if (string.Equals(scanResult.LastEntryType, "assistant", StringComparison.Ordinal))
            {
                if (scanResult.LastContentTypes.Contains("tool_use", StringComparer.Ordinal))
                {
                    return "thinking";
                }

                if (scanResult.LastContentTypes.Contains("text", StringComparer.Ordinal))
                {
                    return "waiting";
                }

                if (scanResult.LastContentTypes.Contains("thinking", StringComparer.Ordinal))
                {
                    return "thinking";
                }
            }

            if (string.Equals(scanResult.LastEntryType, "progress", StringComparison.Ordinal))
            {
                return "thinking";
            }

            if (string.Equals(scanResult.LastEntryType, "user", StringComparison.Ordinal))
            {
                return "thinking";
            }

            return "idle";
        }

        return "idle";
    }

    private static ScanResult ScanLastEntries(string tail)
    {
        var lines = tail.Split('\n');
        string? lastEntryType = null;
        var lastContentTypes = new List<string>();
        string? lastConversationEntryType = null;
        var lastConversationContentTypes = new List<string>();
        string? lastConversationTimestamp = null;
        var hasRecentError = false;

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }

            var shouldStop = ClassifyEntry(
                line,
                ref lastEntryType,
                lastContentTypes,
                ref lastConversationEntryType,
                lastConversationContentTypes,
                ref lastConversationTimestamp,
                ref hasRecentError);

            if (shouldStop)
            {
                break;
            }
        }

        return new ScanResult(
            lastEntryType,
            lastContentTypes,
            lastConversationEntryType,
            lastConversationContentTypes,
            lastConversationTimestamp,
            hasRecentError);
    }

    /// <summary>
    /// Classifies a JSONL line, updating scan state. Returns true when scanning should stop.
    /// </summary>
    private static bool ClassifyEntry(
        string line,
        ref string? lastEntryType,
        List<string> lastContentTypes,
        ref string? lastConversationEntryType,
        List<string> lastConversationContentTypes,
        ref string? lastConversationTimestamp,
        ref bool hasRecentError)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var entryType = root.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString()
                : null;

            if (entryType is null)
            {
                return false;
            }

            if (string.Equals(entryType, "error", StringComparison.Ordinal))
            {
                hasRecentError = true;
            }

            if (lastEntryType is null)
            {
                lastEntryType = entryType;
                ExtractContentTypes(root, entryType, lastContentTypes);
            }

            if (lastConversationEntryType is null &&
                (string.Equals(entryType, "assistant", StringComparison.Ordinal) ||
                 string.Equals(entryType, "user", StringComparison.Ordinal)))
            {
                lastConversationEntryType = entryType;
                ExtractContentTypes(root, entryType, lastConversationContentTypes);

                if (root.TryGetProperty("timestamp", out var tsEl) &&
                    tsEl.ValueKind == JsonValueKind.String)
                {
                    lastConversationTimestamp = tsEl.GetString();
                }

                return true;
            }
        }
        catch (JsonException)
        {
            // Skip partial/malformed lines
        }

        return false;
    }

    private static double GetConversationElapsed(string? timestamp)
    {
        if (timestamp is null)
        {
            return 0;
        }

        if (!DateTime.TryParse(
                timestamp,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return 0;
        }

        return (DateTime.UtcNow - parsed).TotalSeconds;
    }

    private sealed record ScanResult(
        string? LastEntryType,
        List<string> LastContentTypes,
        string? LastConversationEntryType,
        List<string> LastConversationContentTypes,
        string? LastConversationTimestamp,
        bool HasError);

    private static void ExtractContentTypes(
        JsonElement root,
        string entryType,
        List<string> contentTypes)
    {
        if (!string.Equals(entryType, "assistant", StringComparison.Ordinal))
        {
            return;
        }

        if (!root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in contentEl.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var bt))
            {
                var blockType = bt.GetString();
                if (blockType is not null)
                {
                    contentTypes.Add(blockType);
                }
            }
        }
    }

    private static async Task<SessionTokenUsage> AccumulateTokenUsageAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var perModel = new Dictionary<string, ModelAccumulator>(StringComparer.Ordinal);
        var seenMessageIds = new HashSet<string>(StringComparer.Ordinal);
        long contextTokens = 0;

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0 || line[0] != '{')
                {
                    continue;
                }

                // Quick check: skip lines without "usage" to avoid parsing every line
                if (!line.Contains("\"usage\"", StringComparison.Ordinal))
                {
                    continue;
                }

                AccumulateFromLine(line, perModel, seenMessageIds, ref contextTokens);
            }
        }
        catch (IOException)
        {
            // Return what we have
        }

        return BuildUsage(perModel, contextTokens);
    }

    private static SessionTokenUsage BuildUsage(
        Dictionary<string, ModelAccumulator> perModel,
        long contextTokens)
    {
        var usage = new SessionTokenUsage { ContextTokens = contextTokens };

        var models = new List<ModelUsage>();
        string? dominantModel = null;
        long dominantTokens = -1;

        foreach (var (model, accumulator) in perModel)
        {
            usage.InputTokens += accumulator.InputTokens;
            usage.OutputTokens += accumulator.OutputTokens;
            usage.CacheCreationTokens += accumulator.CacheCreationTokens;
            usage.CacheReadTokens += accumulator.CacheReadTokens;

            var modelName = model.Length == 0 ? null : model;
            var modelTokens = accumulator.TotalTokens;
            var modelCost = TokenCostCalculator.Calculate(
                accumulator.InputTokens,
                accumulator.OutputTokens,
                accumulator.CacheCreationTokens,
                accumulator.CacheReadTokens,
                modelName);

            usage.CostUsd += modelCost;
            models.Add(new ModelUsage(
                modelName,
                accumulator.InputTokens,
                accumulator.OutputTokens,
                accumulator.CacheCreationTokens,
                accumulator.CacheReadTokens,
                modelTokens,
                modelCost));

            if (modelTokens > dominantTokens)
            {
                dominantTokens = modelTokens;
                dominantModel = modelName;
            }
        }

        usage.Model = dominantModel;
        usage.Models = models
            .Where(static model => model.TotalTokens > 0)
            .OrderByDescending(static model => model.TotalTokens)
            .ToList();

        return usage;
    }

    private static void AccumulateFromLine(
        string line,
        Dictionary<string, ModelAccumulator> perModel,
        HashSet<string> seenMessageIds,
        ref long contextTokens)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("message", out var msgEl))
            {
                return;
            }

            if (IsDuplicateMessage(msgEl, seenMessageIds) ||
                !msgEl.TryGetProperty("usage", out var usageEl))
            {
                return;
            }

            var model = msgEl.TryGetProperty("model", out var modelEl) &&
                        modelEl.ValueKind == JsonValueKind.String
                ? modelEl.GetString() ?? string.Empty
                : string.Empty;

            var blockInput = ReadLong(usageEl, "input_tokens");
            var blockOutput = ReadLong(usageEl, "output_tokens");
            var blockCacheCreation = ReadLong(usageEl, "cache_creation_input_tokens");
            var blockCacheRead = ReadLong(usageEl, "cache_read_input_tokens");

            // One assistant message can span several internal API requests, listed in
            // a nested "iterations" array. The top-level input/output only reflect the
            // last request, so sum input/output across iterations to recover the billed
            // amount. Cache read/creation are per-message (identical across iterations),
            // so they are kept from the top-level and not summed.
            var (countedInput, countedOutput) = SumIterationsOrDefault(usageEl, blockInput, blockOutput);

            if (!perModel.TryGetValue(model, out var accumulator))
            {
                accumulator = new ModelAccumulator();
                perModel[model] = accumulator;
            }

            accumulator.InputTokens += countedInput;
            accumulator.OutputTokens += countedOutput;
            accumulator.CacheCreationTokens += blockCacheCreation;
            accumulator.CacheReadTokens += blockCacheRead;

            // Context size of a turn = its prompt tokens (input + cache) for a single
            // request — use the top-level values (not the iteration sum) so the bar
            // reflects the current window, not the cumulative billed input.
            var blockContext = blockInput + blockCacheRead + blockCacheCreation;
            if (blockContext > 0)
            {
                contextTokens = blockContext;
            }
        }
        catch (JsonException)
        {
            // Skip malformed lines
        }
    }

    /// <summary>
    /// Returns true when the message has already been counted. A multi-block assistant
    /// turn is written as several JSONL lines that all repeat the same message.id and
    /// usage object; each unique id must be counted once to avoid inflating totals.
    /// </summary>
    private static bool IsDuplicateMessage(
        JsonElement msgEl,
        HashSet<string> seenMessageIds)
        => msgEl.TryGetProperty("id", out var idEl) &&
           idEl.ValueKind == JsonValueKind.String &&
           idEl.GetString() is { Length: > 0 } messageId &&
           !seenMessageIds.Add(messageId);

    /// <summary>
    /// Sums input/output tokens across a usage block's nested "iterations" array
    /// (each iteration is one internal API request). Falls back to the supplied
    /// top-level values when there is no multi-iteration array.
    /// </summary>
    private static (long Input, long Output) SumIterationsOrDefault(
        JsonElement usageEl,
        long topLevelInput,
        long topLevelOutput)
    {
        if (!usageEl.TryGetProperty("iterations", out var iterationsEl) ||
            iterationsEl.ValueKind != JsonValueKind.Array ||
            iterationsEl.GetArrayLength() <= 1)
        {
            return (topLevelInput, topLevelOutput);
        }

        long input = 0;
        long output = 0;
        foreach (var iteration in iterationsEl.EnumerateArray())
        {
            input += ReadLong(iteration, "input_tokens");
            output += ReadLong(iteration, "output_tokens");
        }

        return (input, output);
    }

    private sealed class ModelAccumulator
    {
        public long InputTokens { get; set; }

        public long OutputTokens { get; set; }

        public long CacheCreationTokens { get; set; }

        public long CacheReadTokens { get; set; }

        public long TotalTokens
            => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;
    }

    private static long ReadLong(
        JsonElement element,
        string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;

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

    private sealed record CachedActivity(
        string Status,
        DateTime LastModifiedUtc);

    private sealed record CachedTokenUsage(
        SessionTokenUsage Usage,
        DateTime LastModifiedUtc);
}