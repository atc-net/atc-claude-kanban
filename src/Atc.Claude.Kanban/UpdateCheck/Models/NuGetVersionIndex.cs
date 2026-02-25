namespace Atc.Claude.Kanban.UpdateCheck.Models;

/// <summary>
/// Represents the version index response from the NuGet v3 flat container API.
/// </summary>
internal sealed class NuGetVersionIndex
{
    /// <summary>
    /// Gets or sets the list of available package versions.
    /// </summary>
    [JsonPropertyName("versions")]
    public required IReadOnlyList<string> Versions { get; set; }
}