namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Information about a Claude Code subagent spawned via the Task tool.
/// Parsed from JSONL transcript files at ~/.claude/projects/{hash}/{sessionId}/subagents/agent-{id}.jsonl.
/// </summary>
public sealed class SubagentInfo
{
    /// <summary>
    /// Gets or sets the 7-character hex agent identifier from the filename.
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; set; }

    /// <summary>
    /// Gets or sets the parent session identifier.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the human-readable slug name from the JSONL metadata.
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets a short description (first user message, truncated to 100 chars).
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the model name (e.g. "claude-opus-4-6").
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the first JSONL entry.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the file modification time of the JSONL file.
    /// </summary>
    [JsonPropertyName("lastActivityAt")]
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subagent is currently active (modified in last 30s).
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the working directory of the subagent.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }
}