namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Result record for add note responses.
/// </summary>
public sealed record AddNoteResult(bool Success, ClaudeTask? Task);