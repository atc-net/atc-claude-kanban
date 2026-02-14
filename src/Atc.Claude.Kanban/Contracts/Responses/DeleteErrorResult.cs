namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Error result for task delete operations that includes blocking task IDs.
/// </summary>
public sealed record DeleteErrorResult(string Error, IReadOnlyList<string>? BlockedTasks);