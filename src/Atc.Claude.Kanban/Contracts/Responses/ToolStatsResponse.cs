namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Aggregated tool usage statistics for a session, returned by the tool-stats endpoint.
/// </summary>
public sealed record ToolStatsResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("totalCalls")] int TotalCalls,
    [property: JsonPropertyName("uniqueTools")] int UniqueTools,
    [property: JsonPropertyName("totalFailed")] int TotalFailed,
    [property: JsonPropertyName("totalRejected")] int TotalRejected,
    [property: JsonPropertyName("tools")] IReadOnlyList<ToolStat> Tools);