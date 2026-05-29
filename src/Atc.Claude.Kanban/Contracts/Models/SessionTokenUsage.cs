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
    /// Gets or sets the context size of the most recent turn (its prompt tokens:
    /// input + cache read + cache creation), used to gauge how full the context
    /// window currently is. Distinct from the cumulative totals above.
    /// </summary>
    [JsonPropertyName("contextTokens")]
    public long ContextTokens { get; set; }

    /// <summary>
    /// Gets or sets the estimated cost in USD.
    /// </summary>
    [JsonPropertyName("costUsd")]
    public double CostUsd { get; set; }

    /// <summary>
    /// Gets or sets the dominant model name (the model with the most tokens),
    /// used as the participant's headline label for cost calculation.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the per-model breakdown of tokens and cost. Contains one
    /// entry when the session used a single model, or several when it switched
    /// models mid-run. Ordered by descending token count.
    /// </summary>
    [JsonPropertyName("models")]
    public IReadOnlyList<ModelUsage> Models { get; set; } = [];
}