namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Query parameters for the sessions listing endpoint.
/// </summary>
public sealed record GetSessionsParameters(
    [FromQuery] int? Limit);