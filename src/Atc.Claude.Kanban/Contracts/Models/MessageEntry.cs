namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Represents a single message entry parsed from a Claude Code JSONL transcript.
/// </summary>
public sealed class MessageEntry
{
    /// <summary>
    /// Gets or sets the message type: "user", "assistant", "tool_use", "tool_result", or "system".
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// Gets or sets the ISO 8601 timestamp of the message.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the truncated text content (up to 500 characters).
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the full untruncated text content.
    /// </summary>
    [JsonPropertyName("fullText")]
    public string? FullText { get; set; }

    /// <summary>
    /// Gets or sets the model name (e.g. "claude-opus-4-6") from assistant messages.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the tool name for tool_use entries.
    /// </summary>
    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    /// <summary>
    /// Gets or sets the tool input parameters for tool_use entries.
    /// </summary>
    [JsonPropertyName("toolInput")]
    public Dictionary<string, object>? ToolInput { get; set; }

    /// <summary>
    /// Gets or sets the tool result content (truncated to 1500 characters).
    /// </summary>
    [JsonPropertyName("toolResult")]
    public string? ToolResult { get; set; }

    /// <summary>
    /// Gets or sets the unique tool use identifier for correlating tool_use with tool_result.
    /// </summary>
    [JsonPropertyName("toolUseId")]
    public string? ToolUseId { get; set; }

    /// <summary>
    /// Gets or sets the agent identifier for Agent tool_use blocks.
    /// </summary>
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    /// <summary>
    /// Gets or sets the unique message identifier from the JSONL entry.
    /// </summary>
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }
}