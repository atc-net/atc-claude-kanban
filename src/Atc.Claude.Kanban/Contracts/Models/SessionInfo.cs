namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Aggregated session information returned by the /api/sessions endpoint.
/// </summary>
public sealed class SessionInfo
{
    /// <summary>
    /// Gets or sets the session identifier (directory name under ~/.claude/tasks/).
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the human-readable session name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the project path from the sessions-index.
    /// </summary>
    [JsonPropertyName("project")]
    public string? Project { get; set; }

    /// <summary>
    /// Gets or sets the session description from the sessions-index.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the git branch name from the sessions-index.
    /// </summary>
    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; set; }

    /// <summary>
    /// Gets or sets the total number of tasks in this session.
    /// </summary>
    [JsonPropertyName("taskCount")]
    public int TaskCount { get; set; }

    /// <summary>
    /// Gets or sets the number of pending tasks.
    /// </summary>
    [JsonPropertyName("pending")]
    public int Pending { get; set; }

    /// <summary>
    /// Gets or sets the number of in-progress tasks.
    /// </summary>
    [JsonPropertyName("inProgress")]
    public int InProgress { get; set; }

    /// <summary>
    /// Gets or sets the number of completed tasks.
    /// </summary>
    [JsonPropertyName("completed")]
    public int Completed { get; set; }

    /// <summary>
    /// Gets or sets the completion percentage (0-100).
    /// </summary>
    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a plan file exists for this session.
    /// </summary>
    [JsonPropertyName("hasPlan")]
    public bool HasPlan { get; set; }

    /// <summary>
    /// Gets or sets the plan title extracted from the plan markdown.
    /// </summary>
    [JsonPropertyName("planTitle")]
    public string? PlanTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this session is a team session.
    /// </summary>
    [JsonPropertyName("isTeam")]
    public bool IsTeam { get; set; }

    /// <summary>
    /// Gets or sets the number of team members.
    /// </summary>
    [JsonPropertyName("memberCount")]
    public int MemberCount { get; set; }

    /// <summary>
    /// Gets or sets the last modification time of any task in this session.
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public DateTime ModifiedAt { get; set; }

    /// <summary>
    /// Gets or sets the session creation time from the sessions-index.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the session slug (typically same as Id).
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether all tasks have been cleaned up (session is done).
    /// </summary>
    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Gets or sets the peak task count from the .highwatermark file.
    /// </summary>
    [JsonPropertyName("peakTaskCount")]
    public int PeakTaskCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of subagent JSONL files found.
    /// </summary>
    [JsonPropertyName("subagentCount")]
    public int SubagentCount { get; set; }

    /// <summary>
    /// Gets or sets the number of subagents modified in the last 30 seconds.
    /// </summary>
    [JsonPropertyName("activeSubagentCount")]
    public int ActiveSubagentCount { get; set; }
}