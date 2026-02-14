namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route parameters for plan-related endpoints.
/// </summary>
public sealed record SlugParameters(
    [FromRoute] string Slug);