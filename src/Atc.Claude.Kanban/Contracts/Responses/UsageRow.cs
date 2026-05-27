namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Token usage and estimated cost for one participant in a session — either the
/// lead session itself or one of its subagents.
/// </summary>
public sealed record UsageRow(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("totalTokens")] long TotalTokens,
    [property: JsonPropertyName("costUsd")] double CostUsd);