namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Discovers and parses Claude Code subagent JSONL transcript files
/// from ~/.claude/projects/{hash}/{sessionId}/subagents/agent-*.jsonl.
/// Uses <see cref="IMemoryCache"/> with 10-second TTL.
/// </summary>
public sealed class SubagentService
{
    private const int LastMessageMaxLength = 200;
    private const int TailReadSize = 5120;

    // A structured result is far larger than a text reply (several KB) and is not the final
    // transcript line, so it needs its own window rather than the last-message tail size.
    private const int StructuredResultReadSize = 262144;
    private const int StructuredResultFieldMaxLength = 80;

    private static readonly TimeSpan ActiveThreshold = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(90);

    private readonly string claudeDir;
    private readonly IMemoryCache cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubagentService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="cache">Memory cache for subagent metadata.</param>
    public SubagentService(
        string claudeDir,
        IMemoryCache cache)
    {
        this.claudeDir = claudeDir;
        this.cache = cache;
    }

    /// <summary>
    /// Returns all subagents for a session by scanning JSONL transcript files.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of subagent information.</returns>
    public async Task<IReadOnlyList<SubagentInfo>> GetSubagentsForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"subagents:{sessionId}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<SubagentInfo>? cached) && cached is not null)
        {
            return cached;
        }

        var subagentFiles = FindSubagentFiles(sessionId);
        if (subagentFiles.Count == 0)
        {
            cache.Set(cacheKey, (IReadOnlyList<SubagentInfo>)[], TimeSpan.FromSeconds(10));
            return [];
        }

        var subagents = new List<SubagentInfo>();

        foreach (var file in subagentFiles)
        {
            var info = await ParseSubagentFileAsync(file, sessionId, cancellationToken);
            if (info is not null)
            {
                subagents.Add(info);
            }
        }

        EnrichAgentInfo(sessionId, subagents);

        var result = (IReadOnlyList<SubagentInfo>)subagents;
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(10));
        return result;
    }

    /// <summary>
    /// Returns lightweight subagent counts for a session without full JSONL parsing.
    /// Only counts files and checks modification times. The active count uses the same
    /// threshold as the "active" status in the detailed listing, so an idle agent — one that
    /// has stopped writing but is not yet aged out — does not keep its session in the
    /// active filter.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>A tuple of (total count, active count).</returns>
    public (int Total, int Active) GetSubagentCounts(string sessionId)
    {
        var files = FindSubagentFiles(sessionId);
        if (files.Count == 0)
        {
            return (0, 0);
        }

        var now = DateTime.UtcNow;
        var active = 0;

        foreach (var file in files)
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (now - lastWrite < ActiveThreshold)
                {
                    active++;
                }
            }
            catch (IOException)
            {
                // Skip inaccessible files
            }
        }

        return (files.Count, active);
    }

    private List<string> FindSubagentFiles(string sessionId)
    {
        var projectsDir = Path.Combine(claudeDir, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return [];
        }

        var result = new List<string>();

        foreach (var hashDir in Directory.GetDirectories(projectsDir))
        {
            var subagentsDir = Path.Combine(hashDir, sessionId, "subagents");
            if (!Directory.Exists(subagentsDir))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(subagentsDir, "agent-*.jsonl"))
            {
                result.Add(file);
            }

            // Workflow-spawned subagents live one level deeper, under
            // subagents/workflows/{runId}/agent-*.jsonl. Enumerate those run directories
            // explicitly rather than recursing, so this stays bounded on the session-scan
            // hot path; the agent-* pattern also excludes the run's journal.jsonl.
            var workflowsDir = Path.Combine(subagentsDir, "workflows");
            if (!Directory.Exists(workflowsDir))
            {
                continue;
            }

            foreach (var runDir in Directory.GetDirectories(workflowsDir))
            {
                foreach (var file in Directory.GetFiles(runDir, "agent-*.jsonl"))
                {
                    result.Add(file);
                }
            }
        }

        return result;
    }

    private static async Task<SubagentInfo?> ParseSubagentFileAsync(
        string filePath,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var agentId = fileName.StartsWith("agent-", StringComparison.Ordinal)
            ? fileName["agent-".Length..]
            : fileName;

        DateTime lastActivityAt;

        try
        {
            lastActivityAt = File.GetLastWriteTimeUtc(filePath);
        }
        catch (IOException)
        {
            return null;
        }

        var elapsed = DateTime.UtcNow - lastActivityAt;
        var status = elapsed < ActiveThreshold ? "active"
                   : elapsed < IdleThreshold ? "idle"
                   : "stopped";

        var metadata = await ReadSubagentMetadataAsync(filePath, cancellationToken);

        // Only read a result for non-active agents to avoid reading files mid-write
        if (!string.Equals(status, "active", StringComparison.Ordinal))
        {
            metadata.LastMessage = await ReadAgentResultAsync(filePath, cancellationToken);
        }

        var workflowRunId = ResolveWorkflowRunId(filePath);

        // A workflow agent is spawned by the Workflow runtime rather than the Agent tool, so the
        // parent transcript holds no toolUseResult record to enrich from. Derive the equivalent
        // stats from the agent's own transcript so its row is not left blank.
        var (toolUses, durationMs) = workflowRunId is null
            ? (null, null)
            : await DeriveWorkflowStatsAsync(filePath, metadata.StartedAt, cancellationToken);

        return new SubagentInfo
        {
            AgentId = agentId,
            SessionId = sessionId,
            Slug = metadata.Slug,
            Description = metadata.Description,
            Model = metadata.Model,
            StartedAt = metadata.StartedAt,
            LastActivityAt = lastActivityAt,
            Status = status,
            LastMessage = metadata.LastMessage,
            Cwd = metadata.Cwd,
            TranscriptPath = filePath,
            TranscriptDir = Path.GetDirectoryName(filePath),
            WorkflowRunId = workflowRunId,
            ToolUses = toolUses,
            DurationMs = durationMs,
        };
    }

    /// <summary>
    /// Returns an agent's result: its last assistant text, or — for a schema'd workflow agent that
    /// ends on a forced StructuredOutput call and never emits text — that structured result.
    /// </summary>
    private static async Task<string?> ReadAgentResultAsync(
        string filePath,
        CancellationToken cancellationToken)
        => await ReadLastMessageAsync(filePath, cancellationToken)
           ?? await ReadStructuredResultAsync(filePath, cancellationToken);

    /// <summary>
    /// Derives a workflow agent's tool count and active duration from its own transcript, standing
    /// in for the Agent-tool completion record that workflow-spawned agents never produce.
    /// </summary>
    private static async Task<(int? ToolUses, long? DurationMs)> DeriveWorkflowStatsAsync(
        string filePath,
        DateTime? startedAt,
        CancellationToken cancellationToken)
    {
        var stats = await ReadWorkflowStatsAsync(filePath, cancellationToken);

        var durationMs = startedAt is not null && stats.LastTimestamp is not null
            ? (long?)(stats.LastTimestamp.Value - startedAt.Value).TotalMilliseconds
            : null;

        return (stats.ToolUses, durationMs);
    }

    /// <summary>
    /// Scans a workflow agent's transcript for the stats the Agent-tool completion record would
    /// otherwise supply: the number of tool calls it made and the timestamp of its final entry.
    /// Cold path only — reached from the subagent list, never from the session-scan counts.
    /// </summary>
    private static async Task<(int? ToolUses, DateTime? LastTimestamp)> ReadWorkflowStatsAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var toolUses = 0;
        DateTime? lastTimestamp = null;

        try
        {
            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.Length == 0 || line[0] != '{')
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // Parsed exactly like the metadata timestamps this is subtracted from
                    // (see ParseJsonlEntry); mixing DateTime kinds would skew the duration.
                    if (root.TryGetProperty("timestamp", out var tsEl) &&
                        tsEl.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(tsEl.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ts))
                    {
                        lastTimestamp = ts;
                    }

                    toolUses += CountToolUseBlocks(root);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (IOException)
        {
            return (toolUses > 0 ? toolUses : null, lastTimestamp);
        }

        return (toolUses > 0 ? toolUses : null, lastTimestamp);
    }

    /// <summary>
    /// Counts tool_use blocks in an assistant entry's content array.
    /// </summary>
    private static int CountToolUseBlocks(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var entryTypeEl) ||
            !string.Equals(entryTypeEl.GetString(), "assistant", StringComparison.Ordinal) ||
            !root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = 0;
        foreach (var block in contentEl.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var blockTypeEl) &&
                string.Equals(blockTypeEl.GetString(), "tool_use", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the workflow run identifier for a transcript stored at
    /// <c>subagents/workflows/{runId}/agent-*.jsonl</c>, or null for a regular subagent
    /// transcript that sits directly in <c>subagents/</c>.
    /// </summary>
    private static string? ResolveWorkflowRunId(string filePath)
    {
        var runDir = Path.GetDirectoryName(filePath);
        if (runDir is null)
        {
            return null;
        }

        var parentName = Path.GetFileName(Path.GetDirectoryName(runDir));
        return string.Equals(parentName, "workflows", StringComparison.Ordinal)
            ? Path.GetFileName(runDir)
            : null;
    }

    private static async Task<SubagentMetadata> ReadSubagentMetadataAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var metadata = new SubagentMetadata();

        try
        {
            using var reader = new StreamReader(filePath);
            for (var i = 0; i < 10; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrEmpty(line) || line[0] != '{')
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    ParseJsonlEntry(doc.RootElement, metadata);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (IOException)
        {
            // Return what we have
        }

        return metadata;
    }

    private static void ParseJsonlEntry(
        JsonElement root,
        SubagentMetadata metadata)
    {
        // Extract timestamp from any entry for startedAt
        if (metadata.StartedAt is null &&
            root.TryGetProperty("timestamp", out var tsElement) &&
            tsElement.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(tsElement.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ts))
        {
            metadata.StartedAt = ts;
        }

        // Extract cwd from any entry
        if (metadata.Cwd is null &&
            root.TryGetProperty("cwd", out var cwdElement) &&
            cwdElement.ValueKind == JsonValueKind.String)
        {
            metadata.Cwd = cwdElement.GetString();
        }

        var entryType = root.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;

        if (string.Equals(entryType, "user", StringComparison.Ordinal))
        {
            ExtractUserMetadata(root, metadata);
        }

        // Extract model from assistant entries
        if (metadata.Model is null &&
            string.Equals(entryType, "assistant", StringComparison.Ordinal) &&
            root.TryGetProperty("message", out var assistantMsg) &&
            assistantMsg.TryGetProperty("model", out var modelElement) &&
            modelElement.ValueKind == JsonValueKind.String)
        {
            metadata.Model = modelElement.GetString();
        }
    }

    private static void ExtractUserMetadata(
        JsonElement root,
        SubagentMetadata metadata)
    {
        if (metadata.Slug is null &&
            root.TryGetProperty("slug", out var slugElement) &&
            slugElement.ValueKind == JsonValueKind.String)
        {
            metadata.Slug = slugElement.GetString();
        }

        if (metadata.Description is null &&
            root.TryGetProperty("message", out var msgElement) &&
            msgElement.TryGetProperty("content", out var contentElement) &&
            contentElement.ValueKind == JsonValueKind.String)
        {
            var content = contentElement.GetString();
            if (!string.IsNullOrEmpty(content))
            {
                metadata.Description = CleanAgentDescription(content);
            }
        }
    }

    /// <summary>
    /// Reads the tail of a JSONL transcript file to extract the last assistant message content.
    /// Seeks from the end to avoid reading the entire file.
    /// </summary>
    private static async Task<string?> ReadLastMessageAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var fileLength = stream.Length;
            if (fileLength == 0)
            {
                return null;
            }

            var readSize = (int)System.Math.Min(TailReadSize, fileLength);
            stream.Seek(-readSize, SeekOrigin.End);

            var buffer = new byte[readSize];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken);
            var tail = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

            return FindLastAssistantMessage(tail);
        }
        catch (IOException)
        {
            // File may have been deleted or locked
            return null;
        }
    }

    /// <summary>
    /// Reads the tail of a transcript looking for the last StructuredOutput tool call and
    /// renders its input compactly. Uses a larger window than the last-message read because a
    /// structured result routinely exceeds it, and the call is not always the final line.
    /// Cold path only — reached from the subagent list, never from the session-scan counts.
    /// </summary>
    private static async Task<string?> ReadStructuredResultAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var fileLength = stream.Length;
            if (fileLength == 0)
            {
                return null;
            }

            var readSize = (int)System.Math.Min(StructuredResultReadSize, fileLength);
            var start = fileLength - readSize;
            stream.Seek(start, SeekOrigin.Begin);

            var buffer = new byte[readSize];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken);
            var tail = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Reading from an offset can start mid-line; drop that partial head outright so a
            // truncated record can never be mistaken for a malformed one.
            if (start > 0)
            {
                var firstNewline = tail.IndexOf('\n', StringComparison.Ordinal);
                tail = firstNewline < 0 ? string.Empty : tail[(firstNewline + 1)..];
            }

            return FindLastStructuredResult(tail);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Scans backwards for the last line holding a StructuredOutput tool call and renders its
    /// input as a compact single line. The call is located by name rather than by position,
    /// because trailing entries follow it in the transcript.
    /// </summary>
    private static string? FindLastStructuredResult(string tail)
    {
        var lines = tail.Split('\n');

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 ||
                line[0] != '{' ||
                !line.Contains("\"StructuredOutput\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var result = ExtractStructuredResult(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the StructuredOutput tool_use block in an assistant entry and flattens its input
    /// to "key: value" pairs. Only scalar fields are rendered, each clipped, so the summary
    /// stays readable within the last-message length budget.
    /// </summary>
    private static string? ExtractStructuredResult(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "tool_use", StringComparison.Ordinal) ||
                !block.TryGetProperty("name", out var nameEl) ||
                !string.Equals(nameEl.GetString(), "StructuredOutput", StringComparison.Ordinal) ||
                !block.TryGetProperty("input", out var inputEl) ||
                inputEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var parts = new List<string>();
            foreach (var field in inputEl.EnumerateObject())
            {
                var value = field.Value.ValueKind switch
                {
                    JsonValueKind.String => field.Value.GetString(),
                    JsonValueKind.Number => field.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var clipped = value.Length > StructuredResultFieldMaxLength
                    ? string.Concat(value.AsSpan(0, StructuredResultFieldMaxLength), "...")
                    : value;

                parts.Add($"{field.Name}: {clipped.ReplaceLineEndings(" ")}");
            }

            if (parts.Count == 0)
            {
                continue;
            }

            var text = string.Join(" · ", parts);
            return text.Length > LastMessageMaxLength
                ? string.Concat(text.AsSpan(0, LastMessageMaxLength), "...")
                : text;
        }

        return null;
    }

    /// <summary>
    /// Searches backwards through JSONL lines to find the last assistant message text.
    /// </summary>
    private static string? FindLastAssistantMessage(string tail)
    {
        var lines = tail.Split('\n');

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeEl) ||
                    !string.Equals(typeEl.GetString(), "assistant", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!root.TryGetProperty("message", out var msgEl) ||
                    !msgEl.TryGetProperty("content", out var contentEl))
                {
                    continue;
                }

                var text = ExtractAssistantText(contentEl);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                return text.Length > LastMessageMaxLength
                    ? string.Concat(text.AsSpan(0, LastMessageMaxLength), "...")
                    : text;
            }
            catch (JsonException)
            {
                // Skip malformed lines (including partial first line from seek)
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts text content from an assistant message content field.
    /// Handles both string content and array-of-content-blocks format.
    /// </summary>
    private static string? ExtractAssistantText(JsonElement contentElement)
    {
        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString();
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var block in contentElement.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var blockType) ||
                !string.Equals(blockType.GetString(), "text", StringComparison.Ordinal) ||
                !block.TryGetProperty("text", out var textEl) ||
                textEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = textEl.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(text);
            }
        }

        return parts.Count > 0 ? string.Join(' ', parts) : null;
    }

    /// <summary>
    /// Enriches subagent entries with agent names extracted from Agent tool_use blocks
    /// in the parent session's JSONL file. Matches by correlating tool_use.id with
    /// agent_progress.parentToolUseID entries. Also detects rejected (Esc'd) and
    /// killed agents and forces their status to "stopped" so orphans don't linger
    /// as active in the UI.
    /// </summary>
    private void EnrichAgentInfo(
        string sessionId,
        List<SubagentInfo> subagents)
    {
        if (subagents.Count == 0)
        {
            return;
        }

        var sessionJsonlPath = FindSessionJsonlPath(sessionId);
        if (sessionJsonlPath is null)
        {
            return;
        }

        try
        {
            var digest = BuildAgentMaps(sessionJsonlPath);
            foreach (var agent in subagents)
            {
                if (digest.Names.TryGetValue(agent.AgentId, out var name))
                {
                    agent.AgentName = name;
                }

                if (digest.Descriptions.TryGetValue(agent.AgentId, out var desc))
                {
                    agent.AgentDescription = desc;
                }

                if (digest.StoppedAgentIds.Contains(agent.AgentId))
                {
                    agent.Status = "stopped";
                }

                if (digest.Usage.TryGetValue(agent.AgentId, out var usage))
                {
                    agent.ToolUses = usage.ToolUses;
                    agent.DurationMs = usage.DurationMs;
                }
            }
        }
        catch (IOException)
        {
            // Parent session file may be inaccessible
        }
    }

    /// <summary>
    /// Aggregated agent metadata derived from a single parent-JSONL scan: names and
    /// descriptions per agent, the set of agents that were rejected or killed, and
    /// per-agent completion usage (tool count + duration) from the toolUseResult line.
    /// </summary>
    private sealed record SessionAgentDigest(
        Dictionary<string, string> Names,
        Dictionary<string, string> Descriptions,
        HashSet<string> StoppedAgentIds,
        Dictionary<string, (int? ToolUses, long? DurationMs)> Usage);

    /// <summary>
    /// Builds mappings of agentId to agent name and description by scanning the parent session JSONL
    /// for Agent tool_use blocks, agent_progress entries, and toolUseResult entries (foreground agents).
    /// Also collects rejected tool_use ids and killed/errored task ids so callers can mark those
    /// agents as stopped.
    /// </summary>
    private static SessionAgentDigest BuildAgentMaps(string jsonlPath)
    {
        var nameByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        var descByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        var agentIdByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        var rejectedToolUseIds = new HashSet<string>(StringComparer.Ordinal);
        var killedAgentIds = new HashSet<string>(StringComparer.Ordinal);
        var usageByAgentId = new Dictionary<string, (int? ToolUses, long? DurationMs)>(StringComparer.Ordinal);

        ScanSessionJsonl(jsonlPath, nameByToolUseId, descByToolUseId, agentIdByToolUseId, rejectedToolUseIds, killedAgentIds, usageByAgentId);

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (toolUseId, agentId) in agentIdByToolUseId)
        {
            if (nameByToolUseId.TryGetValue(toolUseId, out var name))
            {
                names[agentId] = name;
            }

            if (descByToolUseId.TryGetValue(toolUseId, out var desc))
            {
                descriptions[agentId] = desc;
            }
        }

        var stoppedAgentIds = new HashSet<string>(killedAgentIds, StringComparer.Ordinal);
        foreach (var toolUseId in rejectedToolUseIds)
        {
            if (agentIdByToolUseId.TryGetValue(toolUseId, out var agentId))
            {
                stoppedAgentIds.Add(agentId);
            }
        }

        return new SessionAgentDigest(names, descriptions, stoppedAgentIds, usageByAgentId);
    }

    private static void ScanSessionJsonl(
        string jsonlPath,
        Dictionary<string, string> nameByToolUseId,
        Dictionary<string, string> descByToolUseId,
        Dictionary<string, string> agentIdByToolUseId,
        HashSet<string> rejectedToolUseIds,
        HashSet<string> killedAgentIds,
        Dictionary<string, (int? ToolUses, long? DurationMs)> usageByAgentId)
    {
        using var reader = new StreamReader(jsonlPath);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }

            if (line.Contains("\"agent_progress\"", StringComparison.Ordinal))
            {
                ExtractAgentProgressMapping(line, agentIdByToolUseId);
            }
            else if (line.Contains("\"toolUseResult\"", StringComparison.Ordinal) &&
                     line.Contains("\"agentId\"", StringComparison.Ordinal) &&
                     line.Contains("\"tool_result\"", StringComparison.Ordinal))
            {
                ExtractToolUseResultMapping(line, agentIdByToolUseId, usageByAgentId);
            }
            else if (line.Contains("\"Agent\"", StringComparison.Ordinal) &&
                     line.Contains("\"tool_use\"", StringComparison.Ordinal))
            {
                ExtractAgentToolUseInfo(line, nameByToolUseId, descByToolUseId);
            }

            if (line.Contains("User rejected tool use", StringComparison.Ordinal) &&
                line.Contains("\"tool_use_id\"", StringComparison.Ordinal))
            {
                var toolUseId = ExtractJsonStringValue(line, "tool_use_id");
                if (toolUseId is not null)
                {
                    rejectedToolUseIds.Add(toolUseId);
                }
            }

            if (line.Contains("<task-notification>", StringComparison.Ordinal) &&
                (line.Contains("<status>killed</status>", StringComparison.Ordinal) ||
                 line.Contains("<status>error</status>", StringComparison.Ordinal)))
            {
                var taskId = ExtractTaskId(line);
                if (taskId is not null)
                {
                    killedAgentIds.Add(taskId);
                }
            }
        }
    }

    /// <summary>
    /// Pulls the agent id out of a &lt;task-id&gt;...&lt;/task-id&gt; element embedded in a JSONL line.
    /// </summary>
    private static string? ExtractTaskId(string line)
    {
        const string openTag = "<task-id>";
        const string closeTag = "</task-id>";
        var start = line.IndexOf(openTag, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += openTag.Length;
        var end = line.IndexOf(closeTag, start, StringComparison.Ordinal);
        return end > start ? line[start..end] : null;
    }

    private static void ExtractAgentProgressMapping(
        string line,
        Dictionary<string, string> agentIdByToolUseId)
    {
        // Fields may be nested at varying depths — use string extraction
        var agentId = ExtractJsonStringValue(line, "agentId");
        var parentToolUseId = ExtractJsonStringValue(line, "parentToolUseID");

        if (agentId is not null && parentToolUseId is not null)
        {
            agentIdByToolUseId.TryAdd(parentToolUseId, agentId);
        }
    }

    /// <summary>
    /// Extracts foreground agent correlation from toolUseResult entries.
    /// These map tool_result.tool_use_id → toolUseResult.agentId for agents spawned in the foreground,
    /// and capture the completion usage (totalToolUseCount/totalDurationMs) per agent.
    /// </summary>
    private static void ExtractToolUseResultMapping(
        string line,
        Dictionary<string, string> agentIdByToolUseId,
        Dictionary<string, (int? ToolUses, long? DurationMs)> usageByAgentId)
    {
        var agentId = ExtractJsonStringValue(line, "agentId");
        if (agentId is null)
        {
            return;
        }

        // Extract tool_use_id from the tool_result content block
        var toolUseId = ExtractJsonStringValue(line, "tool_use_id");
        if (toolUseId is not null)
        {
            agentIdByToolUseId.TryAdd(toolUseId, agentId);
        }

        // Capture the completion usage reported on this line (tokens are derived from the
        // transcript elsewhere; here we only keep the tool count and active duration).
        var toolUses = ExtractJsonNumberValue(line, "totalToolUseCount");
        var durationMs = ExtractJsonNumberValue(line, "totalDurationMs");
        if ((toolUses is not null || durationMs is not null) && !usageByAgentId.ContainsKey(agentId))
        {
            usageByAgentId[agentId] = ((int?)toolUses, durationMs);
        }
    }

    /// <summary>
    /// Extracts the first occurrence of a JSON integer value by key name from raw text.
    /// Returns null when the key is absent or the value is not an integer.
    /// </summary>
    private static long? ExtractJsonNumberValue(
        string line,
        string key)
    {
        var marker = $"\"{key}\":";
        var idx = line.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var start = idx + marker.Length;
        while (start < line.Length && line[start] == ' ')
        {
            start++;
        }

        var end = start;
        if (end < line.Length && line[end] == '-')
        {
            end++;
        }

        while (end < line.Length && char.IsDigit(line[end]))
        {
            end++;
        }

        return long.TryParse(line.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Extracts the first occurrence of a JSON string value by key name from raw text.
    /// Avoids full JSON parsing for performance on large JSONL files.
    /// </summary>
    private static string? ExtractJsonStringValue(
        string line,
        string key)
    {
        var marker = $"\"{key}\":\"";
        var idx = line.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var start = idx + marker.Length;
        var end = line.IndexOf('"', start);
        return end > start ? line[start..end] : null;
    }

    private static void ExtractAgentToolUseInfo(
        string line,
        Dictionary<string, string> nameByToolUseId,
        Dictionary<string, string> descByToolUseId)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("message", out var msgEl) ||
                !msgEl.TryGetProperty("content", out var contentEl) ||
                contentEl.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var block in contentEl.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var bt) ||
                    !string.Equals(bt.GetString(), "tool_use", StringComparison.Ordinal) ||
                    !block.TryGetProperty("name", out var nameEl) ||
                    !string.Equals(nameEl.GetString(), "Agent", StringComparison.Ordinal))
                {
                    continue;
                }

                var toolUseId = block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (toolUseId is null || !block.TryGetProperty("input", out var inputEl))
                {
                    continue;
                }

                if (inputEl.TryGetProperty("name", out var anEl))
                {
                    var agentName = anEl.GetString();
                    if (agentName is not null)
                    {
                        nameByToolUseId[toolUseId] = agentName;
                    }
                }

                if (inputEl.TryGetProperty("description", out var adEl))
                {
                    var agentDesc = adEl.GetString();
                    if (agentDesc is not null)
                    {
                        descByToolUseId[toolUseId] = agentDesc;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Skip malformed lines
        }
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

    /// <summary>
    /// Strips Claude Code protocol tags (e.g. &lt;teammate-message&gt;) from subagent
    /// descriptions, preferring the summary attribute when present.
    /// </summary>
    private static string CleanAgentDescription(string content)
    {
        // Extract summary from <teammate-message summary="..."> if present
        const string marker = "summary=\"";
        if (!content.StartsWith('<') ||
            !content.Contains(marker, StringComparison.Ordinal))
        {
            return content;
        }

        var start = content.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = content.IndexOf('"', start);
        return end > start
            ? content[start..end]
            : content[start..];
    }
}