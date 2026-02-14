namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Result record for task update responses.
/// </summary>
public sealed record UpdateResult(bool Success, ClaudeTask? Task);