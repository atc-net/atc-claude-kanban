namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Represents a single selectable option for an AskUserQuestion question.
/// </summary>
public sealed class AnswerOption
{
    /// <summary>
    /// Gets or sets the option label shown to the user.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the longer description explaining the option.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}