namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// A phase declared in a workflow script's meta block. Phases describe the run's intended
/// shape; which agent ran in which phase is runtime-only and never persisted.
/// </summary>
public sealed class WorkflowPhase
{
    /// <summary>
    /// Gets or sets the phase title.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; set; }

    /// <summary>
    /// Gets or sets the optional phase detail line.
    /// </summary>
    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }
}