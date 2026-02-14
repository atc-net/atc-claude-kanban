namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route parameters for endpoints that require a session identifier.
/// </summary>
public sealed record SessionIdParameters(
    [FromRoute] string SessionId);