namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Metadata extracted from the first 250 lines of a session JSONL transcript file.
/// </summary>
public sealed record JsonlMetadata
{
    /// <summary>
    /// Gets the working directory recorded in the transcript.
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Gets the git branch name recorded in the transcript.
    /// </summary>
    public string? GitBranch { get; init; }

    /// <summary>
    /// Gets the session slug (human-readable name used in plan filenames).
    /// </summary>
    public string? Slug { get; init; }

    /// <summary>
    /// Gets the parent session identifier for child/subagent JSONL files.
    /// </summary>
    public string? ParentSessionId { get; init; }

    /// <summary>
    /// Gets the custom title set via Claude Code's /rename command.
    /// </summary>
    public string? CustomTitle { get; init; }
}