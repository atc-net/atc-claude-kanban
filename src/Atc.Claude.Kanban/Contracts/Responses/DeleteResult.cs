namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Result record for task delete responses.
/// </summary>
public sealed record DeleteResult(bool Success, string? TaskId);