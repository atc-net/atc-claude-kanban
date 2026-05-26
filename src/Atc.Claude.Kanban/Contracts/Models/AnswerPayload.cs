namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Captures the structured questions and selected answers recorded for an
/// AskUserQuestion tool call in a session transcript.
/// </summary>
public sealed class AnswerPayload
{
    /// <summary>
    /// Gets or sets the questions presented to the user.
    /// </summary>
    [JsonPropertyName("questions")]
    public List<AnswerQuestion>? Questions { get; set; }

    /// <summary>
    /// Gets or sets the selected answers keyed by question text. Each value is
    /// the list of chosen option labels (a single-select question has one entry).
    /// </summary>
    [JsonPropertyName("answers")]
    public Dictionary<string, List<string>>? Answers { get; set; }
}