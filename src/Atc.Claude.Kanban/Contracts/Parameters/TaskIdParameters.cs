namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route parameters for endpoints that require session and task identifiers.
/// </summary>
public sealed record TaskIdParameters(
    [FromRoute] string SessionId,
    [FromRoute] string TaskId);