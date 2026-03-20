namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route and query parameters for subagent message endpoints.
/// </summary>
public sealed record GetSubagentMessagesParameters(
    [FromRoute] string SessionId,
    [FromRoute] string AgentId,
    [FromQuery] int? Limit);