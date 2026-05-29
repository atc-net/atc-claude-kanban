namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Token usage and estimated cost attributed to a single model within a
/// participant's transcript. A session that switches models mid-run (for
/// example Opus to Haiku for a background task) produces one entry per model.
/// </summary>
public sealed record ModelUsage(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("inputTokens")] long InputTokens,
    [property: JsonPropertyName("outputTokens")] long OutputTokens,
    [property: JsonPropertyName("cacheCreationTokens")] long CacheCreationTokens,
    [property: JsonPropertyName("cacheReadTokens")] long CacheReadTokens,
    [property: JsonPropertyName("totalTokens")] long TotalTokens,
    [property: JsonPropertyName("costUsd")] double CostUsd);