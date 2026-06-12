namespace Atc.Claude.Kanban.Helpers;

/// <summary>
/// Parses Claude Code &lt;task-notification&gt; envelopes (background-agent completion
/// records delivered as user messages) into structured fields.
/// </summary>
/// <remarks>
/// &lt;result&gt; and &lt;usage&gt; carry unescaped agent text that may itself contain literal
/// <c>&lt;/result&gt;</c> or a fake <c>&lt;usage&gt;</c> block (e.g. an agent describing this very
/// format). The real closing tags are always the LAST occurrence, so those bodies are
/// extracted greedily and the usage numbers are read from the LAST matching tag.
/// </remarks>
internal static class TaskNotificationParser
{
    /// <summary>
    /// Parses the envelope into structured fields.
    /// </summary>
    /// <param name="text">The raw user-message content.</param>
    /// <returns>The parsed fields, or null when the text is not a task-notification.</returns>
    internal static TaskNotificationInfo? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) ||
            !text.Contains("<task-notification>", StringComparison.Ordinal))
        {
            return null;
        }

        var summary = FirstBetween(text, "summary")?.Trim();
        var status = FirstBetween(text, "status")?.Trim();
        var result = LastBetween(text, "result")?.Trim();

        long? subagentTokens = null;
        int? toolUses = null;
        long? durationMs = null;
        var usageRaw = LastBetween(text, "usage");
        if (usageRaw is not null)
        {
            subagentTokens = LastLongValue(usageRaw, "subagent_tokens");
            toolUses = LastIntValue(usageRaw, "tool_uses");
            durationMs = LastLongValue(usageRaw, "duration_ms");
        }

        return new TaskNotificationInfo(summary, status, result, subagentTokens, toolUses, durationMs);
    }

    /// <summary>
    /// Formats a compact usage suffix for a chip label, e.g. <c> · 22.3k tok · 6 tools · 119s</c>.
    /// </summary>
    /// <param name="info">The parsed notification.</param>
    /// <returns>The usage suffix, or an empty string when no usage numbers are present.</returns>
    internal static string FormatUsage(TaskNotificationInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var parts = new List<string>(3);
        if (info.SubagentTokens is { } tokens)
        {
            parts.Add(FormatTokens(tokens) + " tok");
        }

        if (info.ToolUses is { } tools)
        {
            parts.Add($"{tools.ToString(CultureInfo.InvariantCulture)} {(tools == 1 ? "tool" : "tools")}");
        }

        if (info.DurationMs is { } durationMs)
        {
            var seconds = (long)System.Math.Round(durationMs / 1000.0, MidpointRounding.AwayFromZero);
            parts.Add($"{seconds.ToString(CultureInfo.InvariantCulture)}s");
        }

        return parts.Count > 0 ? " · " + string.Join(" · ", parts) : string.Empty;
    }

    private static string FormatTokens(long tokens)
    {
        if (tokens < 1000)
        {
            return tokens.ToString(CultureInfo.InvariantCulture);
        }

        var thousands = (tokens / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
        if (thousands.EndsWith(".0", StringComparison.Ordinal))
        {
            thousands = thousands[..^2];
        }

        return thousands + "k";
    }

    // First <tag>…</tag> (non-greedy): safe for single-line metadata that precedes <result>.
    private static string? FirstBetween(
        string text,
        string tag)
    {
        var open = $"<{tag}>";
        var close = $"</{tag}>";
        var start = text.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += open.Length;
        var end = text.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : text[start..end];
    }

    // First <tag> open to the LAST </tag> close (greedy): for <result>/<usage> whose body
    // may contain literal markers an agent typed while describing the format.
    private static string? LastBetween(
        string text,
        string tag)
    {
        var open = $"<{tag}>";
        var close = $"</{tag}>";
        var start = text.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += open.Length;
        var end = text.LastIndexOf(close, StringComparison.Ordinal);
        return end < start ? null : text[start..end];
    }

    private static long? LastLongValue(
        string text,
        string tag)
        => LastTagValue(text, tag) is { } raw &&
           long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? LastIntValue(
        string text,
        string tag)
        => LastTagValue(text, tag) is { } raw &&
           int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // The LAST <tag>…</tag> value: the real usage block wins over an example an agent
    // embedded earlier in its result.
    private static string? LastTagValue(
        string text,
        string tag)
    {
        var open = $"<{tag}>";
        var close = $"</{tag}>";
        var openIdx = text.LastIndexOf(open, StringComparison.Ordinal);
        if (openIdx < 0)
        {
            return null;
        }

        var start = openIdx + open.Length;
        var end = text.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : text[start..end].Trim();
    }
}