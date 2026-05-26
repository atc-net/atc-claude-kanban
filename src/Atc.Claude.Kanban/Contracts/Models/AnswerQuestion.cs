namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Represents a single question posed by an AskUserQuestion tool call.
/// </summary>
public sealed class AnswerQuestion
{
    /// <summary>
    /// Gets or sets the question text.
    /// </summary>
    [JsonPropertyName("question")]
    public string? Question { get; set; }

    /// <summary>
    /// Gets or sets the selectable options presented for the question.
    /// </summary>
    [JsonPropertyName("options")]
    public List<AnswerOption>? Options { get; set; }
}