namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// A workflow script's source, returned for the source view.
/// </summary>
public sealed record WorkflowSourceResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("content")] string Content);