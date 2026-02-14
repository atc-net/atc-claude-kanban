namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route parameters for endpoints that require a team name.
/// </summary>
public sealed record TeamNameParameters(
    [FromRoute] string Name);