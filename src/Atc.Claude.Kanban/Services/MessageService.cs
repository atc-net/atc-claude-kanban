namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Parses Claude Code JSONL transcript files to extract recent conversation messages.
/// Uses tail-reading with an adaptive buffer to avoid loading entire files.
/// Results are cached with a 5-second TTL and file modification time check.
/// </summary>
public sealed class MessageService
{
    private const int DefaultLimit = 15;
    private const int InitialBufferSize = 65536;
    private const int MaxBufferSize = 1048576;
    private const int TextTruncateLength = 500;
    private const int ToolResultTruncateLength = 1500;
    private const string NotASystemMessage = "__normal__";

    private readonly string claudeDir;
    private readonly IMemoryCache cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="cache">Memory cache for parsed messages.</param>
    public MessageService(
        string claudeDir,
        IMemoryCache cache)
    {
        this.claudeDir = claudeDir;
        this.cache = cache;
    }

    /// <summary>
    /// Returns recent messages from a session's JSONL transcript file.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="limit">Maximum number of messages to return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of message entries, most recent last.</returns>
    public async Task<IReadOnlyList<MessageEntry>> GetRecentMessagesAsync(
        string sessionId,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var jsonlPath = FindSessionJsonlPath(sessionId);
        if (jsonlPath is null)
        {
            return [];
        }

        return await ReadMessagesFromFileAsync(jsonlPath, limit, $"messages:{sessionId}:{limit}", cancellationToken);
    }

    /// <summary>
    /// Returns recent messages from a subagent's JSONL transcript file.
    /// </summary>
    /// <param name="sessionId">The parent session identifier.</param>
    /// <param name="agentId">The subagent identifier.</param>
    /// <param name="limit">Maximum number of messages to return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of message entries, most recent last.</returns>
    public async Task<IReadOnlyList<MessageEntry>> GetSubagentMessagesAsync(
        string sessionId,
        string agentId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var jsonlPath = FindSubagentJsonlPath(sessionId, agentId);
        if (jsonlPath is null)
        {
            return [];
        }

        return await ReadMessagesFromFileAsync(jsonlPath, limit, $"messages:{sessionId}:{agentId}:{limit}", cancellationToken);
    }

    private async Task<IReadOnlyList<MessageEntry>> ReadMessagesFromFileAsync(
        string jsonlPath,
        int limit,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        // Check cache with mtime validation
        DateTime currentLastModifiedUtc;
        try
        {
            currentLastModifiedUtc = File.GetLastWriteTimeUtc(jsonlPath);
        }
        catch (IOException)
        {
            return [];
        }

        if (cache.TryGetValue(cacheKey, out CachedMessages? cached) &&
            cached is not null &&
            cached.LastModifiedUtc == currentLastModifiedUtc &&
            cached.Limit >= limit)
        {
            return cached.Messages;
        }

        var messages = await TailReadMessagesAsync(jsonlPath, limit, cancellationToken);

        cache.Set(cacheKey, new CachedMessages(messages, currentLastModifiedUtc, limit), TimeSpan.FromSeconds(5));
        return messages;
    }

    /// <summary>
    /// Reads the tail of a JSONL file, parsing messages with an adaptive buffer
    /// that doubles when the requested limit is not met.
    /// </summary>
    private static async Task<IReadOnlyList<MessageEntry>> TailReadMessagesAsync(
        string filePath,
        int limit,
        CancellationToken cancellationToken)
    {
        var bufferSize = InitialBufferSize;

        while (bufferSize <= MaxBufferSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var messages = await ReadTailWithBufferAsync(filePath, bufferSize, cancellationToken);
            if (messages.Count >= limit || bufferSize >= MaxBufferSize)
            {
                // Return the last 'limit' messages (most recent)
                if (messages.Count > limit)
                {
                    return messages.GetRange(messages.Count - limit, limit);
                }

                return messages;
            }

            bufferSize = bufferSize * 2;
        }

        return [];
    }

    private static async Task<List<MessageEntry>> ReadTailWithBufferAsync(
        string filePath,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var fileLength = stream.Length;
            if (fileLength == 0)
            {
                return [];
            }

            var readSize = (int)System.Math.Min(bufferSize, fileLength);
            var offset = fileLength - readSize;
            if (offset > 0)
            {
                stream.Seek(offset, SeekOrigin.Begin);
            }

            var buffer = new byte[readSize];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken);
            var tail = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

            return ParseJsonlMessages(tail, skipFirstLine: offset > 0);
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>
    /// Parses JSONL lines into message entries, correlating tool_use blocks with
    /// their corresponding tool_result blocks.
    /// </summary>
    /// <param name="content">The raw JSONL text content to parse.</param>
    /// <param name="skipFirstLine">Whether to skip the first line (may be partial from tail seek).</param>
    /// <returns>A list of parsed message entries in chronological order.</returns>
    internal static List<MessageEntry> ParseJsonlMessages(
        string content,
        bool skipFirstLine)
    {
        var lines = content.Split('\n');
        var startIndex = skipFirstLine ? 1 : 0;

        // Parse all valid JSON lines once, collecting tool results and entries together
        var parsed = new List<(JsonDocument Doc, string Line)>();
        var toolResults = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Quick guard: valid JSONL objects always start with '{'
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }

            JsonDocument? doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // Skip malformed lines (partial from tail seek, truncated writes)
                continue;
            }

            var root = doc.RootElement;

            // Collect tool results from user messages in the same pass
            if (root.TryGetProperty("type", out var typeEl) &&
                string.Equals(typeEl.GetString(), "user", StringComparison.Ordinal))
            {
                CollectToolResultsFromEntry(root, toolResults);
            }

            parsed.Add((doc, line));
        }

        var messages = BuildMessageEntries(parsed, toolResults);
        return messages;
    }

    private static List<MessageEntry> BuildMessageEntries(
        List<(JsonDocument Doc, string Line)> parsed,
        Dictionary<string, string> toolResults)
    {
        var messages = new List<MessageEntry>();

        foreach (var (doc, _) in parsed)
        {
            var root = doc.RootElement;

            var entryType = root.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString()
                : null;

            var timestamp = root.TryGetProperty("timestamp", out var tsEl)
                ? tsEl.GetString()
                : null;

            var uuid = root.TryGetProperty("uuid", out var uuidEl)
                ? uuidEl.GetString()
                : null;

            if (string.Equals(entryType, "user", StringComparison.Ordinal))
            {
                ParseUserEntry(root, timestamp, uuid, messages);
            }
            else if (string.Equals(entryType, "assistant", StringComparison.Ordinal))
            {
                ParseAssistantEntry(root, timestamp, uuid, toolResults, messages);
            }

            doc.Dispose();
        }

        // Deduplicate consecutive "Compacted" messages
        for (var i = messages.Count - 1; i > 0; i--)
        {
            if (string.Equals(messages[i].SystemLabel, "Compacted", StringComparison.Ordinal) &&
                string.Equals(messages[i - 1].SystemLabel, "Compacted", StringComparison.Ordinal))
            {
                messages.RemoveAt(i);
            }
        }

        return messages;
    }

    private static void CollectToolResultsFromEntry(
        JsonElement root,
        Dictionary<string, string> toolResults)
    {
        if (!root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var blockType) ||
                !string.Equals(blockType.GetString(), "tool_result", StringComparison.Ordinal))
            {
                continue;
            }

            if (!block.TryGetProperty("tool_use_id", out var toolUseIdEl))
            {
                continue;
            }

            var toolUseId = toolUseIdEl.GetString();
            if (toolUseId is null)
            {
                continue;
            }

            var resultText = ExtractToolResultText(block);
            if (resultText is not null)
            {
                toolResults[toolUseId] = resultText;
            }
        }
    }

    private static string? ExtractToolResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var contentEl))
        {
            return null;
        }

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            return contentEl.GetString();
        }

        if (contentEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var part in contentEl.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textEl) &&
                textEl.ValueKind == JsonValueKind.String)
            {
                var text = textEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    parts.Add(text);
                }
            }
        }

        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    private static void ParseUserEntry(
        JsonElement root,
        string? timestamp,
        string? uuid,
        List<MessageEntry> messages)
    {
        var isMeta = root.TryGetProperty("isMeta", out var metaEl) &&
                     metaEl.ValueKind == JsonValueKind.True;

        var text = ExtractUserMessageText(root);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var label = GetSystemMessageLabel(text);

        // null = skip entirely (e.g. /clear, session continuation)
        if (label is null)
        {
            return;
        }

        // isMeta messages without a recognized label are internal — skip
        if (isMeta && string.Equals(label, NotASystemMessage, StringComparison.Ordinal))
        {
            return;
        }

        // Recognized system message — show with label
        if (!string.Equals(label, NotASystemMessage, StringComparison.Ordinal))
        {
            messages.Add(new MessageEntry
            {
                Type = "user",
                Timestamp = timestamp,
                Text = label,
                SystemLabel = label,
                Uuid = uuid,
            });

            return;
        }

        messages.Add(new MessageEntry
        {
            Type = "user",
            Timestamp = timestamp,
            Text = Truncate(text, TextTruncateLength),
            FullText = text,
            Uuid = uuid,
        });
    }

    /// <summary>
    /// Extracts the text content from a user message entry,
    /// handling both string and array content formats.
    /// </summary>
    private static string? ExtractUserMessageText(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl))
        {
            return null;
        }

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            return contentEl.GetString();
        }

        if (contentEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var textParts = new List<string>();
        foreach (var block in contentEl.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var bt) &&
                string.Equals(bt.GetString(), "text", StringComparison.Ordinal) &&
                block.TryGetProperty("text", out var txEl) &&
                txEl.ValueKind == JsonValueKind.String)
            {
                var part = txEl.GetString();
                if (!string.IsNullOrEmpty(part))
                {
                    textParts.Add(part);
                }
            }
        }

        return textParts.Count > 0 ? string.Join("\n", textParts) : null;
    }

    /// <summary>
    /// Determines a system message label for special user messages.
    /// Returns a label string for display, or null to skip the message entirely.
    /// </summary>
    private static string? GetSystemMessageLabel(string text)
    {
        // XML-structured system messages
        var xmlLabel = GetXmlSystemLabel(text);
        if (xmlLabel is not null)
        {
            return xmlLabel;
        }

        // Session continuation — skip entirely
        if (text.StartsWith("This session is being continued from a previous conversation", StringComparison.Ordinal))
        {
            return null;
        }

        // /clear command — skip entirely
        if (text.Contains("<command-name>/clear</command-name>", StringComparison.Ordinal))
        {
            return null;
        }

        // /compact command
        if (text.Contains("<command-name>/compact</command-name>", StringComparison.Ordinal))
        {
            return "Compacted";
        }

        return NotASystemMessage;
    }

    /// <summary>
    /// Checks for XML-structured system message patterns (summaries, task notifications,
    /// command output, etc.) and returns the appropriate label.
    /// </summary>
    private static string? GetXmlSystemLabel(string text)
    {
        var summaryValue = ExtractXmlTagContent(text, "summary");
        if (summaryValue is not null)
        {
            return summaryValue.Trim();
        }

        if (text.Contains("<task-notification>", StringComparison.Ordinal))
        {
            var statusValue = ExtractXmlTagContent(text, "status");
            return statusValue is not null
                ? $"Background task {statusValue}"
                : "Background task notification";
        }

        if (text.Contains("<local-command-stdout>", StringComparison.Ordinal))
        {
            return text.Contains("Compacted", StringComparison.Ordinal)
                ? "Compacted"
                : "Command output";
        }

        if (text.Contains("<local-command-caveat>", StringComparison.Ordinal))
        {
            return "System notification";
        }

        if (text.Contains(".output completed", StringComparison.Ordinal) &&
            text.Contains("Background command", StringComparison.Ordinal))
        {
            return "Background task completed";
        }

        return null;
    }

    /// <summary>
    /// Extracts text content between a simple XML open/close tag pair.
    /// Returns null if the tag is not found.
    /// </summary>
    private static string? ExtractXmlTagContent(
        string text,
        string tagName)
    {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";
        var startIdx = text.IndexOf(openTag, StringComparison.Ordinal);
        if (startIdx < 0)
        {
            return null;
        }

        startIdx += openTag.Length;
        var endIdx = text.IndexOf(closeTag, startIdx, StringComparison.Ordinal);
        if (endIdx < 0)
        {
            return null;
        }

        return text[startIdx..endIdx];
    }

    private static void ParseAssistantEntry(
        JsonElement root,
        string? timestamp,
        string? uuid,
        Dictionary<string, string> toolResults,
        List<MessageEntry> messages)
    {
        if (!root.TryGetProperty("message", out var msgEl))
        {
            return;
        }

        var model = msgEl.TryGetProperty("model", out var modelEl)
            ? modelEl.GetString()
            : null;

        if (!msgEl.TryGetProperty("content", out var contentEl))
        {
            return;
        }

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            var text = contentEl.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                messages.Add(new MessageEntry
                {
                    Type = "assistant",
                    Timestamp = timestamp,
                    Text = Truncate(text, TextTruncateLength),
                    FullText = text,
                    Model = model,
                    Uuid = uuid,
                });
            }

            return;
        }

        if (contentEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var blockTypeEl))
            {
                continue;
            }

            var blockType = blockTypeEl.GetString();

            if (string.Equals(blockType, "text", StringComparison.Ordinal))
            {
                ParseTextBlock(block, timestamp, uuid, model, messages);
            }
            else if (string.Equals(blockType, "tool_use", StringComparison.Ordinal))
            {
                ParseToolUseBlock(block, timestamp, uuid, model, toolResults, messages);
            }
        }
    }

    private static void ParseTextBlock(
        JsonElement block,
        string? timestamp,
        string? uuid,
        string? model,
        List<MessageEntry> messages)
    {
        if (!block.TryGetProperty("text", out var textEl) ||
            textEl.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var text = textEl.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        messages.Add(new MessageEntry
        {
            Type = "assistant",
            Timestamp = timestamp,
            Text = Truncate(text, TextTruncateLength),
            FullText = text,
            Model = model,
            Uuid = uuid,
        });
    }

    private static void ParseToolUseBlock(
        JsonElement block,
        string? timestamp,
        string? uuid,
        string? model,
        Dictionary<string, string> toolResults,
        List<MessageEntry> messages)
    {
        var toolName = block.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()
            : null;

        var toolUseId = block.TryGetProperty("id", out var idEl)
            ? idEl.GetString()
            : null;

        var toolInput = ExtractToolInput(block);

        string? toolResult = null;
        if (toolUseId is not null && toolResults.TryGetValue(toolUseId, out var result))
        {
            toolResult = Truncate(result, ToolResultTruncateLength);
        }

        var displayText = BuildToolDisplayText(toolName, toolInput);

        messages.Add(new MessageEntry
        {
            Type = "tool_use",
            Timestamp = timestamp,
            Text = displayText,
            ToolName = toolName,
            ToolInput = toolInput,
            ToolResult = toolResult,
            ToolUseId = toolUseId,
            Model = model,
            Uuid = uuid,
        });
    }

    private static Dictionary<string, object>? ExtractToolInput(
        JsonElement block)
    {
        if (!block.TryGetProperty("input", out var inputEl) ||
            inputEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var prop in inputEl.EnumerateObject())
        {
            var value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => (object?)prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => prop.Value.GetRawText(),
            };

            if (value is not null)
            {
                result[prop.Name] = value;
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Builds a human-readable display text for a tool use entry.
    /// </summary>
    private static string BuildToolDisplayText(
        string? toolName,
        Dictionary<string, object>? input)
    {
        if (toolName is null)
        {
            return "Unknown tool";
        }

        if (input is null)
        {
            return toolName;
        }

        return toolName switch
        {
            "Read" when input.TryGetValue("file_path", out var fp) => $"Read {fp}",
            "Edit" when input.TryGetValue("file_path", out var fp) => $"Edit {fp}",
            "Write" when input.TryGetValue("file_path", out var fp) => $"Write {fp}",
            "Grep" when input.TryGetValue("pattern", out var p) => $"Grep \"{p}\"",
            "Glob" when input.TryGetValue("pattern", out var p) => $"Glob \"{p}\"",
            "Bash" when input.TryGetValue("command", out var c) => $"Bash: {Truncate(c.ToString(), 100)}",
            "Agent" or "Task" when input.TryGetValue("description", out var d) => $"Agent: {Truncate(d.ToString(), 100)}",
            "WebFetch" when input.TryGetValue("url", out var u) => $"WebFetch {u}",
            "WebSearch" when input.TryGetValue("query", out var q) => $"WebSearch \"{q}\"",
            _ => toolName,
        };
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

    private string? FindSubagentJsonlPath(
        string sessionId,
        string agentId)
    {
        var projectsDir = Path.Combine(claudeDir, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return null;
        }

        foreach (var hashDir in Directory.GetDirectories(projectsDir))
        {
            var jsonlFile = Path.Combine(hashDir, sessionId, "subagents", $"agent-{agentId}.jsonl");
            if (File.Exists(jsonlFile))
            {
                return jsonlFile;
            }
        }

        return null;
    }

    private static string? Truncate(
        string? text,
        int maxLength)
    {
        if (text is null || text.Length <= maxLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, maxLength), "...");
    }

    private sealed record CachedMessages(
        IReadOnlyList<MessageEntry> Messages,
        DateTime LastModifiedUtc,
        int Limit);
}