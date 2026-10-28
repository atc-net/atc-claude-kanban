namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// One stretch of time a task spent in progress, from the status change that started it
/// to the one that ended it. <see cref="End"/> is null while the task is still in progress.
/// </summary>
public sealed record TaskTimelineSegment(
    [property: JsonPropertyName("start")] DateTime Start,
    [property: JsonPropertyName("end")] DateTime? End);