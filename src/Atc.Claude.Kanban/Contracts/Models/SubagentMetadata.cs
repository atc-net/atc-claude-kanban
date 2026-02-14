namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Metadata extracted from the first few lines of a subagent JSONL transcript file.
/// </summary>
internal sealed record SubagentMetadata
{
    public string? Slug { get; set; }

    public string? Description { get; set; }

    public string? Model { get; set; }

    public DateTime? StartedAt { get; set; }

    public string? Cwd { get; set; }
}