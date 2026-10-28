namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// The recorded history of one task: when it was created and each stretch it spent in progress.
/// A task that was reopened has one segment per run.
/// </summary>
public sealed record TaskTimelineEntry(
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("createdAt")] DateTime? CreatedAt,
    [property: JsonPropertyName("segments")] IReadOnlyList<TaskTimelineSegment> Segments);