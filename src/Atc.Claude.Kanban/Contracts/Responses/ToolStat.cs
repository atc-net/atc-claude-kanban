namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Per-tool usage statistics for a session.
/// </summary>
public sealed record ToolStat(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("success")] int Success,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("rejected")] int Rejected,
    [property: JsonPropertyName("impact")] int Impact);