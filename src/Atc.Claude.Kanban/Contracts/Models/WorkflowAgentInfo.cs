namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// One agent in a workflow run's roster. Metadata comes from the agent's transcript, while
/// the run status comes from the run journal — the only record of whether an agent finished.
/// </summary>
public sealed class WorkflowAgentInfo
{
    /// <summary>
    /// Gets or sets the agent identifier.
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; set; }

    /// <summary>
    /// Gets or sets the model the agent ran on.
    /// </summary>
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the agent's task description, taken from its first prompt.
    /// </summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the run status: "done" once the journal records a result, otherwise "running".
    /// Journal-derived rather than timestamp-derived, so a finished run does not read as stopped.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the agent's first transcript entry.
    /// </summary>
    [JsonPropertyName("startedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets how long the agent was active, in milliseconds.
    /// </summary>
    [JsonPropertyName("durationMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; set; }

    /// <summary>
    /// Gets or sets the number of tool calls the agent made.
    /// </summary>
    [JsonPropertyName("toolUses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ToolUses { get; set; }

    /// <summary>
    /// Gets or sets the agent's result — its final text, or the structured result when the
    /// agent was given an output schema.
    /// </summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; set; }
}