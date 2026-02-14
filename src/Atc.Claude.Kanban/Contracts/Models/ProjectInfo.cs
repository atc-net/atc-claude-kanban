namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// A distinct project path with its most recent modification time.
/// </summary>
public sealed class ProjectInfo
{
    /// <summary>
    /// Gets or sets the project directory path.
    /// </summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>
    /// Gets or sets the most recent task modification time for this project.
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public DateTime? ModifiedAt { get; set; }
}