namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Wrapper for the sessions-index.json file format.
/// </summary>
public sealed class SessionIndexFile
{
    /// <summary>
    /// Gets or sets the file format version.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the session entries.
    /// </summary>
    [JsonPropertyName("entries")]
    public IReadOnlyList<SessionIndex>? Entries { get; init; }

    /// <summary>
    /// Gets or sets the original project path.
    /// </summary>
    [JsonPropertyName("originalPath")]
    public string? OriginalPath { get; set; }
}