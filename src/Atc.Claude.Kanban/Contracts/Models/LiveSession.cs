namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// A live session entry from Claude Code's session registry at
/// ~/.claude/sessions/{pid}.json. Used to resolve which live interactive session
/// owns an auto-created self-team when the team's recorded leadSessionId is a
/// resumed/ghost id that no longer maps to a discoverable session.
/// </summary>
public sealed class LiveSession
{
    /// <summary>
    /// Gets or sets the session identifier.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets the kind of session (e.g. "interactive").
    /// </summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>
    /// Gets or sets the working directory the session runs in.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    /// <summary>
    /// Gets or sets the session start time as Unix milliseconds.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public long StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the session status (e.g. "busy", "idle").
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}