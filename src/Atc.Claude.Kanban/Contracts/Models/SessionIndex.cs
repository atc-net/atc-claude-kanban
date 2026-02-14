namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// An entry from the sessions-index.json file under ~/.claude/projects/{hash}/.
/// </summary>
public sealed class SessionIndex
{
    /// <summary>
    /// Gets or sets the session identifier.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the project path.
    /// </summary>
    [JsonPropertyName("projectPath")]
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    /// <summary>
    /// Gets or sets the git branch name.
    /// </summary>
    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; set; }

    /// <summary>
    /// Gets or sets the session summary/description.
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the first prompt text.
    /// </summary>
    [JsonPropertyName("firstPrompt")]
    public string? FirstPrompt { get; set; }

    /// <summary>
    /// Gets or sets the session creation timestamp.
    /// </summary>
    [JsonPropertyName("created")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last modification timestamp.
    /// </summary>
    [JsonPropertyName("modified")]
    public DateTime? LastActivityAt { get; set; }

    /// <summary>
    /// Gets or sets the session slug (human-readable name used in plan filenames).
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the custom title set via Claude Code's /rename command.
    /// Stored in JSONL as a <c>type: "custom-title"</c> entry.
    /// </summary>
    [JsonIgnore]
    public string? CustomTitle { get; set; }
}