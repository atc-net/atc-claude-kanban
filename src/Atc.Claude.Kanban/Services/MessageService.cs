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
    private const string InterruptMarker = "[Request interrupted by user]";

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
    /// Returns a page of messages older than the given timestamp for backward pagination.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="limit">Maximum number of messages to return.</param>
    /// <param name="beforeTimestamp">ISO 8601 timestamp; only messages before this are returned.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="MessagesResponse"/> with messages and a hasMore flag.</returns>
    [SuppressMessage("AsyncUsage", "AsyncFixer02:Long-running or blocking operations inside an async method", Justification = "In-memory LINQ on already-awaited collection.")]
    public async Task<MessagesResponse> GetMessagesPageAsync(
        string sessionId,
        int limit,
        string beforeTimestamp,
        CancellationToken cancellationToken = default)
    {
        var jsonlPath = FindSessionJsonlPath(sessionId);
        if (jsonlPath is null)
        {
            return new MessagesResponse([], false);
        }

        // Read a large batch and filter by timestamp
        var fetchLimit = System.Math.Max(limit * 5, 100);
        var allMessages = await ReadMessagesFromFileAsync(
            jsonlPath,
            fetchLimit,
            $"messages-page:{sessionId}:{fetchLimit}",
            cancellationToken);

        var filtered = allMessages
            .Where(m => string.Compare(m.Timestamp, beforeTimestamp, StringComparison.Ordinal) < 0)
            .ToList();

        var hasMore = filtered.Count > limit;
        var page = hasMore
            ? filtered.GetRange(filtered.Count - limit, limit)
            : filtered;

        return new MessagesResponse(page, hasMore);
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
        var answerPayloads = new Dictionary<string, AnswerPayload>(StringComparer.Ordinal);

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
                CollectToolResultsFromEntry(root, toolResults, answerPayloads);
            }

            parsed.Add((doc, line));
        }

        var messages = BuildMessageEntries(parsed, toolResults, answerPayloads);
        return messages;
    }

    private static List<MessageEntry> BuildMessageEntries(
        List<(JsonDocument Doc, string Line)> parsed,
        Dictionary<string, string> toolResults,
        Dictionary<string, AnswerPayload> answerPayloads)
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
                ParseAssistantEntry(root, timestamp, uuid, toolResults, answerPayloads, messages);
            }
            else if (string.Equals(entryType, "queue-operation", StringComparison.Ordinal))
            {
                ParseQueueOperationEntry(root, timestamp, uuid, messages);
            }

            doc.Dispose();
        }

        // Deduplicate consecutive "Compacted" messages, carrying the summary body onto
        // the surviving chip so collapsing never drops the expandable summary.
        for (var i = messages.Count - 1; i > 0; i--)
        {
            if (string.Equals(messages[i].SystemLabel, "Compacted", StringComparison.Ordinal) &&
                string.Equals(messages[i - 1].SystemLabel, "Compacted", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(messages[i - 1].FullText) && !string.IsNullOrEmpty(messages[i].FullText))
                {
                    messages[i - 1].FullText = messages[i].FullText;
                }

                messages.RemoveAt(i);
            }
        }

        return messages;
    }

    private static void CollectToolResultsFromEntry(
        JsonElement root,
        Dictionary<string, string> toolResults,
        Dictionary<string, AnswerPayload> answerPayloads)
    {
        if (!root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        // The structured AskUserQuestion answers live at the line-level
        // toolUseResult rather than inside the tool_result block. Capture once and
        // key by each block id, then attach only to AskUserQuestion tool_use entries.
        var answerPayload = ExtractAnswerPayload(root);

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

            if (answerPayload is not null)
            {
                answerPayloads[toolUseId] = answerPayload;
            }
        }
    }

    private static AnswerPayload? ExtractAnswerPayload(JsonElement root)
    {
        if (!root.TryGetProperty("toolUseResult", out var resultEl) ||
            resultEl.ValueKind != JsonValueKind.Object ||
            !resultEl.TryGetProperty("answers", out var answersEl) ||
            answersEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var answers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var answer in answersEl.EnumerateObject())
        {
            var labels = new List<string>();
            if (answer.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in answer.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } label)
                    {
                        labels.Add(label);
                    }
                }
            }
            else if (answer.Value.ValueKind == JsonValueKind.String && answer.Value.GetString() is { } single)
            {
                labels.Add(single);
            }

            answers[answer.Name] = labels;
        }

        if (answers.Count == 0)
        {
            return null;
        }

        return new AnswerPayload
        {
            Questions = ExtractAnswerQuestions(resultEl),
            Answers = answers,
        };
    }

    private static List<AnswerQuestion>? ExtractAnswerQuestions(
        JsonElement resultEl)
    {
        if (!resultEl.TryGetProperty("questions", out var questionsEl) ||
            questionsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var questions = new List<AnswerQuestion>();
        foreach (var questionEl in questionsEl.EnumerateArray())
        {
            if (questionEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            List<AnswerOption>? options = null;
            if (questionEl.TryGetProperty("options", out var optionsEl) &&
                optionsEl.ValueKind == JsonValueKind.Array)
            {
                options = new List<AnswerOption>();
                foreach (var optionEl in optionsEl.EnumerateArray())
                {
                    if (optionEl.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    options.Add(new AnswerOption
                    {
                        Label = optionEl.TryGetProperty("label", out var labelEl) ? labelEl.GetString() : null,
                        Description = optionEl.TryGetProperty("description", out var descriptionEl) ? descriptionEl.GetString() : null,
                    });
                }
            }

            questions.Add(new AnswerQuestion
            {
                Question = questionEl.TryGetProperty("question", out var questionTextEl) ? questionTextEl.GetString() : null,
                Options = options,
            });
        }

        return questions.Count > 0 ? questions : null;
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
        var images = ExtractUserImages(root);
        var hasText = !string.IsNullOrWhiteSpace(text);

        // Nothing to show: no text and no attachments.
        if (!hasText && images.Count == 0)
        {
            return;
        }

        // Text that maps to a skip/compact/system entry is consumed there; an
        // image-only message falls straight through to the regular entry below.
        if (hasText &&
            !ShouldEmitRegularUserEntry(root, text!, isMeta, timestamp, uuid, messages))
        {
            return;
        }

        messages.Add(new MessageEntry
        {
            Type = "user",
            Timestamp = timestamp,
            Text = hasText ? Truncate(text!, TextTruncateLength) : null,
            FullText = hasText ? text : null,
            Uuid = uuid,
            Images = images.Count > 0 ? images : null,
        });
    }

    /// <summary>
    /// Surfaces a queued user prompt. Queued messages are written as a
    /// "queue-operation"/"enqueue" line with the text at the top-level "content"
    /// (not "message.content") and are never re-emitted as a "user" line, so they
    /// would otherwise be dropped from the Session Log.
    /// </summary>
    private static void ParseQueueOperationEntry(
        JsonElement root,
        string? timestamp,
        string? uuid,
        List<MessageEntry> messages)
    {
        if (!root.TryGetProperty("operation", out var operationEl) ||
            !string.Equals(operationEl.GetString(), "enqueue", StringComparison.Ordinal))
        {
            return;
        }

        var text = root.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String
            ? contentEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Run queued text through the same skip/label pipeline a regular user message
        // gets. A turn can enqueue more than typed prompts — slash commands, interrupt
        // markers, and system notifications (e.g. <task-notification>) also land here,
        // and must not be surfaced verbatim as "You" messages.
        if (!ShouldEmitRegularUserEntry(root, text!, isMeta: false, timestamp, uuid, messages))
        {
            return;
        }

        messages.Add(new MessageEntry
        {
            Type = "user",
            Timestamp = timestamp,
            Text = Truncate(text!, TextTruncateLength),
            FullText = text,
            Uuid = uuid,
            Queued = true,
        });
    }

    /// <summary>
    /// Handles interrupt/compact/system user text. Returns false when the text was
    /// consumed (skipped or emitted as a system entry); true when a regular user
    /// entry should still be produced.
    /// </summary>
    private static bool ShouldEmitRegularUserEntry(
        JsonElement root,
        string text,
        bool isMeta,
        string? timestamp,
        string? uuid,
        List<MessageEntry> messages)
    {
        // Drop messages whose entire body is the interrupt marker, and route
        // inline /compact summaries to a dedicated "Compacted" entry.
        if (ShouldSkipUserText(text) ||
            TryAppendInlineCompactSummary(root, text, timestamp, uuid, messages))
        {
            return false;
        }

        var label = GetSystemMessageLabel(text);

        // null = skip entirely (e.g. /clear, session continuation)
        if (label is null)
        {
            return false;
        }

        // isMeta messages without a recognized label are internal — skip
        if (isMeta && string.Equals(label, NotASystemMessage, StringComparison.Ordinal))
        {
            return false;
        }

        // Recognized system message — show with label, not as a regular entry
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

            return false;
        }

        return true;
    }

    private static List<MessageImage> ExtractUserImages(JsonElement root)
    {
        var images = new List<MessageImage>();
        if (!root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return images;
        }

        var index = 0;
        foreach (var block in contentEl.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeEl) &&
                string.Equals(typeEl.GetString(), "image", StringComparison.Ordinal) &&
                block.TryGetProperty("source", out var sourceEl) &&
                sourceEl.ValueKind == JsonValueKind.Object &&
                sourceEl.TryGetProperty("type", out var sourceTypeEl) &&
                string.Equals(sourceTypeEl.GetString(), "base64", StringComparison.Ordinal))
            {
                var mediaType = sourceEl.TryGetProperty("media_type", out var mediaTypeEl)
                    ? mediaTypeEl.GetString() ?? "image/png"
                    : "image/png";
                images.Add(new MessageImage { BlockIndex = index, MediaType = mediaType });
            }

            index++;
        }

        return images;
    }

    private static bool ShouldSkipUserText(string text)
        => string.Equals(text.Trim(), InterruptMarker, StringComparison.Ordinal);

    /// <summary>
    /// Detects an inline /compact summary entry and appends a "Compacted" message
    /// carrying the stripped summary text. Returns true when the entry was handled.
    /// </summary>
    private static bool TryAppendInlineCompactSummary(
        JsonElement root,
        string text,
        string? timestamp,
        string? uuid,
        List<MessageEntry> messages)
    {
        if (!root.TryGetProperty("isCompactSummary", out var compactEl) ||
            compactEl.ValueKind != JsonValueKind.True)
        {
            return false;
        }

        var summary = StripCompactSummaryPreamble(text);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            messages.Add(new MessageEntry
            {
                Type = "user",
                Timestamp = timestamp,
                Text = "Compacted",
                FullText = summary,
                SystemLabel = "Compacted",
                Uuid = uuid,
            });
        }

        return true;
    }

    /// <summary>
    /// Strips the "This session is being continued..." preamble that Claude Code
    /// prepends to inline /compact summaries.
    /// </summary>
    private static string StripCompactSummaryPreamble(string text)
    {
        const string preamble = "This session is being continued";
        if (!text.StartsWith(preamble, StringComparison.OrdinalIgnoreCase))
        {
            return text.Trim();
        }

        var firstNewline = text.IndexOf('\n', StringComparison.Ordinal);
        if (firstNewline < 0)
        {
            return string.Empty;
        }

        var remainder = text[(firstNewline + 1)..].TrimStart();

        const string summaryHeader = "The summary below";
        if (remainder.StartsWith(summaryHeader, StringComparison.OrdinalIgnoreCase))
        {
            var nextNewline = remainder.IndexOf('\n', StringComparison.Ordinal);
            remainder = nextNewline < 0
                ? string.Empty
                : remainder[(nextNewline + 1)..].TrimStart();
        }

        return remainder.Trim();
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
        // Compaction is rendered as a single expandable "Compacted" chip from the inline
        // compact-summary record. The /compact trigger and the "Compacted (ctrl+o…)" stdout
        // echo are redundant with it, so skip both — otherwise they leave bare markers that
        // stop the summary-bearing chip from collapsing cleanly.
        if (text.Contains("<command-name>/compact</command-name>", StringComparison.Ordinal) ||
            (text.Contains("<local-command-stdout>", StringComparison.Ordinal) &&
             text.Contains("Compacted", StringComparison.Ordinal)))
        {
            return null;
        }

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
        Dictionary<string, AnswerPayload> answerPayloads,
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
                ParseToolUseBlock(block, timestamp, uuid, model, toolResults, answerPayloads, messages);
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
        Dictionary<string, AnswerPayload> answerPayloads,
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

        AnswerPayload? answerPayload = null;
        if (toolUseId is not null &&
            string.Equals(toolName, "AskUserQuestion", StringComparison.Ordinal))
        {
            answerPayloads.TryGetValue(toolUseId, out answerPayload);
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
            AnswerPayload = answerPayload,
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

    /// <summary>
    /// Reads a single base64 image attached to a user message and returns its decoded
    /// bytes and media type, or null when the message or image block cannot be found.
    /// </summary>
    /// <param name="sessionId">The session whose transcript holds the image.</param>
    /// <param name="messageUuid">The uuid of the user message carrying the image.</param>
    /// <param name="blockIndex">The index of the image block within the message content array.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The media type and decoded image bytes, or null when not found.</returns>
    public async Task<(string MediaType, byte[] Data)?> GetUserImageAsync(
        string sessionId,
        string messageUuid,
        int blockIndex,
        CancellationToken cancellationToken = default)
    {
        if (blockIndex < 0 || string.IsNullOrEmpty(messageUuid))
        {
            return null;
        }

        var jsonlPath = FindSessionJsonlPath(sessionId);
        if (jsonlPath is null)
        {
            return null;
        }

        using var reader = new StreamReader(jsonlPath);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0 ||
                line[0] != '{' ||
                !line.Contains(messageUuid, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var image = TryReadUserImage(line, messageUuid, blockIndex);
                if (image is not null)
                {
                    return image;
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines.
            }
        }

        return null;
    }

    private static (string MediaType, byte[] Data)? TryReadUserImage(
        string line,
        string messageUuid,
        int blockIndex)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        if (!root.TryGetProperty("uuid", out var uuidEl) ||
            !string.Equals(uuidEl.GetString(), messageUuid, StringComparison.Ordinal) ||
            !root.TryGetProperty("message", out var msgEl) ||
            !msgEl.TryGetProperty("content", out var contentEl) ||
            contentEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var index = 0;
        foreach (var block in contentEl.EnumerateArray())
        {
            if (index != blockIndex)
            {
                index++;
                continue;
            }

            if (!block.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "image", StringComparison.Ordinal) ||
                !block.TryGetProperty("source", out var sourceEl) ||
                sourceEl.ValueKind != JsonValueKind.Object ||
                !sourceEl.TryGetProperty("data", out var dataEl) ||
                dataEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var data = dataEl.GetString();
            if (string.IsNullOrEmpty(data))
            {
                return null;
            }

            var mediaType = sourceEl.TryGetProperty("media_type", out var mediaTypeEl)
                ? mediaTypeEl.GetString() ?? "image/png"
                : "image/png";

            try
            {
                return (mediaType, Convert.FromBase64String(data));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
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