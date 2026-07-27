namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// A workflow run's state: the phases declared in its script plus the agent roster
/// reconstructed from the run directory. The roster is flat because the mapping of agents to
/// phases exists only at runtime and is never written to disk.
/// </summary>
public sealed record WorkflowRunResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("phases")] IReadOnlyList<WorkflowPhase> Phases,
    [property: JsonPropertyName("agents")] IReadOnlyList<WorkflowAgentInfo> Agents,
    [property: JsonPropertyName("startedCount")] int StartedCount,
    [property: JsonPropertyName("doneCount")] int DoneCount,
    [property: JsonPropertyName("startedAt")] DateTime? StartedAt,
    [property: JsonPropertyName("stoppedAt")] DateTime? StoppedAt);