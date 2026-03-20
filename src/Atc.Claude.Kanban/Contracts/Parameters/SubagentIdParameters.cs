namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route parameters for endpoints that require both a session and subagent identifier.
/// </summary>
public sealed record SubagentIdParameters(
    [FromRoute] string SessionId,
    [FromRoute] string AgentId);