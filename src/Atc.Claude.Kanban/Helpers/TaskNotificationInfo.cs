namespace Atc.Claude.Kanban.Helpers;

/// <summary>
/// Structured fields parsed from a Claude Code &lt;task-notification&gt; envelope
/// (a background-agent completion record delivered as a user message).
/// </summary>
/// <param name="Summary">The human-readable summary (e.g. <c>Agent "Foo" completed</c>).</param>
/// <param name="Status">The task status (e.g. <c>completed</c>, <c>killed</c>, <c>error</c>).</param>
/// <param name="Result">The agent's free-form result text, or null when absent.</param>
/// <param name="SubagentTokens">Total tokens reported in the &lt;usage&gt; block, or null.</param>
/// <param name="ToolUses">Number of tool uses reported in the &lt;usage&gt; block, or null.</param>
/// <param name="DurationMs">Active duration in milliseconds from the &lt;usage&gt; block, or null.</param>
internal sealed record TaskNotificationInfo(
    string? Summary,
    string? Status,
    string? Result,
    long? SubagentTokens,
    int? ToolUses,
    long? DurationMs);