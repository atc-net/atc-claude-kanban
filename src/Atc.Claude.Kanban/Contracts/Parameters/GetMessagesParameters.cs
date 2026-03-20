namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route and query parameters for session message endpoints.
/// </summary>
public sealed record GetMessagesParameters(
    [FromRoute] string SessionId,
    [FromQuery] int? Limit);