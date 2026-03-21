namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Token usage and cost summary for a Claude Code session,
/// accumulated from usage blocks in the JSONL transcript.
/// </summary>
public sealed class SessionTokenUsage
{
    /// <summary>
    /// Gets or sets the total input tokens consumed.
    /// </summary>
    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    /// <summary>
    /// Gets or sets the total output tokens generated.
    /// </summary>
    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    /// <summary>
    /// Gets or sets the total cache creation input tokens.
    /// </summary>
    [JsonPropertyName("cacheCreationTokens")]
    public long CacheCreationTokens { get; set; }

    /// <summary>
    /// Gets or sets the total cache read input tokens.
    /// </summary>
    [JsonPropertyName("cacheReadTokens")]
    public long CacheReadTokens { get; set; }

    /// <summary>
    /// Gets the total tokens (input + output + cache creation + cache read).
    /// </summary>
    [JsonPropertyName("totalTokens")]
    public long TotalTokens
        => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;

    /// <summary>
    /// Gets or sets the estimated cost in USD.
    /// </summary>
    [JsonPropertyName("costUsd")]
    public double CostUsd { get; set; }

    /// <summary>
    /// Gets or sets the model name used for cost calculation.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }
}