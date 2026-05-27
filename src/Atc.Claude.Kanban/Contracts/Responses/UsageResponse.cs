namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Per-participant token/cost breakdown for a session: the lead session plus each
/// subagent, alongside the current context size and overall totals.
/// </summary>
public sealed record UsageResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("contextTokens")] long ContextTokens,
    [property: JsonPropertyName("rows")] IReadOnlyList<UsageRow> Rows,
    [property: JsonPropertyName("totalTokens")] long TotalTokens,
    [property: JsonPropertyName("totalCostUsd")] double TotalCostUsd);