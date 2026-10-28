namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Task status history for a session, read from the task tool results in its transcript.
/// </summary>
public sealed record TaskTimelineResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("tasks")] IReadOnlyList<TaskTimelineEntry> Tasks);