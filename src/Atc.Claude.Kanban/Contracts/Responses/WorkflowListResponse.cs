namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// The workflow scripts belonging to a session, newest first.
/// </summary>
public sealed record WorkflowListResponse(
    [property: JsonPropertyName("workflows")] IReadOnlyList<WorkflowScriptInfo> Workflows);