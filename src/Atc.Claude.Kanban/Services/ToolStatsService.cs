namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Aggregates per-tool usage statistics (call counts, success/failed/rejected
/// outcomes and output-impact share) from a session's JSONL transcript.
/// Results are cached with a file modification time check.
/// </summary>
public sealed class ToolStatsService
{
    private readonly string claudeDir;
    private readonly IMemoryCache cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolStatsService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="cache">Memory cache for computed statistics.</param>
    public ToolStatsService(
        string claudeDir,
        IMemoryCache cache)
    {
        this.claudeDir = claudeDir;
        this.cache = cache;
    }

    /// <summary>
    /// Returns aggregated tool statistics for a session, or null when the
    /// session transcript cannot be found or read.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The tool statistics, or null when the session is unavailable.</returns>
    public async Task<ToolStatsResponse?> GetToolStatsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var jsonlPath = FindSessionJsonlPath(sessionId);
        if (jsonlPath is null)
        {
            return null;
        }

        DateTime lastModifiedUtc;
        try
        {
            lastModifiedUtc = File.GetLastWriteTimeUtc(jsonlPath);
        }
        catch (IOException)
        {
            return null;
        }

        var cacheKey = $"tool-stats:{sessionId}";
        if (cache.TryGetValue(cacheKey, out CachedToolStats? cached) &&
            cached is not null &&
            cached.LastModifiedUtc == lastModifiedUtc)
        {
            return cached.Stats;
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(jsonlPath, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }

        var stats = BuildToolStats(sessionId, content);
        cache.Set(cacheKey, new CachedToolStats(stats, lastModifiedUtc), TimeSpan.FromSeconds(10));
        return stats;
    }

    internal static ToolStatsResponse BuildToolStats(
        string sessionId,
        string content)
    {
        var state = new StatsState();

        foreach (var line in content.Split('\n'))
        {
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                if (string.Equals(type, "assistant", StringComparison.Ordinal))
                {
                    CollectToolUses(root, state);
                }
                else if (string.Equals(type, "user", StringComparison.Ordinal))
                {
                    ApplyToolResults(root, state);
                }
            }
        }

        CountUnresolvedToolUses(state);
        ApproximateSkillImpact(state);
        return ToToolStatsResponse(sessionId, state.ToolMap);
    }

    private static void CollectToolUses(
        JsonElement root,
        StatsState state)
    {
        if (!root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "tool_use", StringComparison.Ordinal) ||
                !block.TryGetProperty("name", out var nameEl) ||
                !block.TryGetProperty("id", out var idEl))
            {
                continue;
            }

            var name = nameEl.GetString();
            var id = idEl.GetString();
            if (name is null || id is null)
            {
                continue;
            }

            var isSkill = string.Equals(name, "Skill", StringComparison.Ordinal);
            state.ToolUseById[id] = (BuildDisplayName(name, isSkill, block), isSkill);
        }
    }

    private static string BuildDisplayName(
        string name,
        bool isSkill,
        JsonElement block)
    {
        if (!block.TryGetProperty("input", out var inputEl) ||
            inputEl.ValueKind != JsonValueKind.Object)
        {
            return name;
        }

        if (isSkill &&
            inputEl.TryGetProperty("skill", out var skillEl) &&
            skillEl.ValueKind == JsonValueKind.String)
        {
            return $"Skill({skillEl.GetString()})";
        }

        if (string.Equals(name, "Agent", StringComparison.Ordinal) &&
            inputEl.TryGetProperty("subagent_type", out var subagentEl) &&
            subagentEl.ValueKind == JsonValueKind.String)
        {
            return $"Agent({subagentEl.GetString()})";
        }

        return name;
    }

    private static void ApplyToolResults(
        JsonElement root,
        StatsState state)
    {
        if (!root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var promptId = root.TryGetProperty("promptId", out var promptIdEl)
            ? promptIdEl.GetString()
            : null;
        var rejected = IsRejected(root);

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "tool_result", StringComparison.Ordinal) ||
                !block.TryGetProperty("tool_use_id", out var idEl))
            {
                continue;
            }

            var id = idEl.GetString();
            if (id is null || !state.ToolUseById.TryGetValue(id, out var info))
            {
                continue;
            }

            state.SeenResults.Add(id);
            RecordResult(state, info, block, promptId, rejected);
        }
    }

    private static void RecordResult(
        StatsState state,
        (string DisplayName, bool IsSkill) info,
        JsonElement block,
        string? promptId,
        bool rejected)
    {
        var accumulator = GetOrAdd(state.ToolMap, info.DisplayName);
        accumulator.Count++;

        var raw = ExtractResultText(block);
        accumulator.OutputBytes += raw.Length;

        if (promptId is not null)
        {
            state.PromptOutputBytes[promptId] = state.PromptOutputBytes.GetValueOrDefault(promptId) + raw.Length;
            if (info.IsSkill)
            {
                if (!state.SkillPromptIds.TryGetValue(promptId, out var names))
                {
                    names = [];
                    state.SkillPromptIds[promptId] = names;
                }

                names.Add(info.DisplayName);
            }
        }

        if (rejected)
        {
            accumulator.Rejected++;
        }
        else if (IsFailure(raw))
        {
            accumulator.Failed++;
        }
        else
        {
            accumulator.Success++;
        }
    }

    private static string ExtractResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var contentEl))
        {
            return string.Empty;
        }

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            return contentEl.GetString() ?? string.Empty;
        }

        if (contentEl.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in contentEl.EnumerateArray())
        {
            if (item.TryGetProperty("text", out var textEl) &&
                textEl.ValueKind == JsonValueKind.String)
            {
                parts.Add(textEl.GetString() ?? string.Empty);
            }
        }

        return string.Join("\n", parts);
    }

    private static bool IsRejected(JsonElement root)
        => root.TryGetProperty("toolUseResult", out var resultEl) &&
           resultEl.ValueKind == JsonValueKind.String &&
           (resultEl.GetString()?.Contains("rejected", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsFailure(string raw)
    {
        if (raw.TrimStart().StartsWith("error", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lower = raw.ToLowerInvariant();
        return ContainsNonZeroExitCode(lower)
            || lower.Contains("command failed", StringComparison.Ordinal)
            || (lower.Contains("failed", StringComparison.Ordinal) && lower.Contains("error", StringComparison.Ordinal));
    }

    private static bool ContainsNonZeroExitCode(string lower)
    {
        const string marker = "exit code ";
        var index = lower.IndexOf(marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var position = index + marker.Length;
            if (position < lower.Length && lower[position] is >= '1' and <= '9')
            {
                return true;
            }

            index = lower.IndexOf(marker, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static void CountUnresolvedToolUses(StatsState state)
    {
        foreach (var (id, info) in state.ToolUseById)
        {
            if (state.SeenResults.Contains(id))
            {
                continue;
            }

            GetOrAdd(state.ToolMap, info.DisplayName).Count++;
        }
    }

    private static void ApproximateSkillImpact(StatsState state)
    {
        foreach (var (promptId, skillNames) in state.SkillPromptIds)
        {
            var turnBytes = state.PromptOutputBytes.GetValueOrDefault(promptId);
            foreach (var name in skillNames)
            {
                if (state.ToolMap.TryGetValue(name, out var accumulator))
                {
                    accumulator.OutputBytes = turnBytes;
                }
            }
        }
    }

    private static ToolStatsResponse ToToolStatsResponse(
        string sessionId,
        Dictionary<string, ToolAccumulator> toolMap)
    {
        var totalCalls = 0;
        var totalFailed = 0;
        var totalRejected = 0;
        long totalOutputBytes = 0;
        foreach (var stats in toolMap.Values)
        {
            totalCalls += stats.Count;
            totalFailed += stats.Failed;
            totalRejected += stats.Rejected;
            totalOutputBytes += stats.OutputBytes;
        }

        var tools = new List<ToolStat>(toolMap.Count);
        foreach (var (name, stats) in toolMap)
        {
            var impact = totalOutputBytes > 0
                ? (int)System.Math.Round((double)stats.OutputBytes / totalOutputBytes * 100)
                : 0;
            tools.Add(new ToolStat(FriendlyToolName(name), stats.Count, stats.Success, stats.Failed, stats.Rejected, impact));
        }

        return new ToolStatsResponse(sessionId, totalCalls, toolMap.Count, totalFailed, totalRejected, tools);
    }

    private static string FriendlyToolName(string name)
    {
        if (!name.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return name;
        }

        var parts = name.Split("__");
        if (parts.Length <= 2)
        {
            return name;
        }

        var joined = string.Join("__", parts.Skip(2));
        return string.IsNullOrEmpty(joined) ? name : joined;
    }

    private static ToolAccumulator GetOrAdd(
        Dictionary<string, ToolAccumulator> toolMap,
        string name)
    {
        if (!toolMap.TryGetValue(name, out var accumulator))
        {
            accumulator = new ToolAccumulator();
            toolMap[name] = accumulator;
        }

        return accumulator;
    }

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

    private sealed class StatsState
    {
        public Dictionary<string, (string DisplayName, bool IsSkill)> ToolUseById { get; } = new(StringComparer.Ordinal);

        public HashSet<string> SeenResults { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ToolAccumulator> ToolMap { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<string>> SkillPromptIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, long> PromptOutputBytes { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ToolAccumulator
    {
        public int Count { get; set; }

        public int Success { get; set; }

        public int Failed { get; set; }

        public int Rejected { get; set; }

        public long OutputBytes { get; set; }
    }

    private sealed record CachedToolStats(ToolStatsResponse Stats, DateTime LastModifiedUtc);
}