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

        var result = (IReadOnlyList<SubagentInfo>)subagents;
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(10));
        return result;
    }

    /// <summary>
    /// Returns lightweight subagent counts for a session without full JSONL parsing.
    /// Only counts files and checks modification times.
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
                if (now - lastWrite < IdleThreshold)
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

        // Only read last message for non-active agents to avoid reading files mid-write
        if (!string.Equals(status, "active", StringComparison.Ordinal))
        {
            metadata.LastMessage = await ReadLastMessageAsync(filePath, cancellationToken);
        }

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
        };
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