namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Result record for plan content responses.
/// </summary>
public sealed record PlanContent(string Content, string Slug);