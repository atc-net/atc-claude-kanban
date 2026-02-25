namespace Atc.Claude.Kanban.UpdateCheck.Models;

/// <summary>
/// Represents the cached result of a NuGet version check,
/// stored in the local application data directory.
/// </summary>
internal sealed class UpdateCheckCache
{
    /// <summary>
    /// Gets or sets the UTC timestamp of the last version check.
    /// </summary>
    [JsonPropertyName("lastCheck")]
    public required DateTimeOffset LastCheck { get; set; }

    /// <summary>
    /// Gets or sets the latest stable version found on NuGet.
    /// </summary>
    [JsonPropertyName("latestVersion")]
    public required string LatestVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an auto-update was successfully performed.
    /// </summary>
    [JsonPropertyName("updatePerformed")]
    public bool UpdatePerformed { get; set; }
}