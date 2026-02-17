namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Represents a single Claude Code task as stored in ~/.claude/tasks/{sessionId}/{taskId}.json.
/// </summary>
public sealed class ClaudeTask
{
    /// <summary>
    /// Gets or sets the unique task identifier within the session.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the task title / subject line.
    /// </summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; set; }

    /// <summary>
    /// Gets or sets the detailed task description (markdown).
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the task status: pending, in_progress, or completed.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets the agent name that owns this task.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>
    /// Gets or sets the present-continuous label shown while the task is in progress.
    /// </summary>
    [JsonPropertyName("activeForm")]
    public string? ActiveForm { get; set; }

    /// <summary>
    /// Gets or sets the IDs of tasks that block this task.
    /// </summary>
    [JsonPropertyName("blockedBy")]
    public IReadOnlyList<string>? BlockedBy { get; set; }

    /// <summary>
    /// Gets or sets the IDs of tasks that this task blocks.
    /// </summary>
    [JsonPropertyName("blocks")]
    public IReadOnlyList<string>? Blocks { get; set; }

    /// <summary>
    /// Gets or sets arbitrary metadata key-value pairs.
    /// </summary>
    [JsonPropertyName("metadata")]
    public IDictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>
    /// Gets or sets the session ID (populated when aggregating across sessions).
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets the human-readable session name (populated when aggregating).
    /// </summary>
    [JsonPropertyName("sessionName")]
    public string? SessionName { get; set; }

    /// <summary>
    /// Gets or sets the project path associated with this task's session.
    /// </summary>
    [JsonPropertyName("project")]
    public string? Project { get; set; }

    /// <summary>
    /// Gets or sets the task creation timestamp (derived from file creation time).
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the task last-updated timestamp (derived from file modification time).
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Returns <see langword="true"/> if this task is an internal agent lifecycle task
    /// (has metadata key "_internal" set to <see langword="true"/>).
    /// </summary>
    [JsonIgnore]
    public bool IsInternal =>
        Metadata is not null &&
        Metadata.TryGetValue("_internal", out var value) &&
        value.ValueKind == JsonValueKind.True;
}