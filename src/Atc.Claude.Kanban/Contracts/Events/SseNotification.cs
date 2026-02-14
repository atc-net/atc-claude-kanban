namespace Atc.Claude.Kanban.Contracts.Events;

/// <summary>
/// A notification sent to SSE clients when file changes are detected.
/// </summary>
public sealed class SseNotification
{
    /// <summary>
    /// Gets or sets the notification type: update, team-update, metadata-update, or plan-update.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// Gets or sets the affected session identifier.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets the affected team name.
    /// </summary>
    [JsonPropertyName("teamName")]
    public string? TeamName { get; set; }

    /// <summary>
    /// Gets or sets the file system event type (add, change, unlink) for task-update notifications.
    /// </summary>
    [JsonPropertyName("event")]
    public string? Event { get; set; }

    /// <summary>
    /// Gets or sets the changed file name (e.g. "2.json") for task-update notifications.
    /// </summary>
    [JsonPropertyName("file")]
    public string? File { get; set; }

    /// <summary>
    /// Gets or sets the plan slug for plan-update notifications.
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the notification timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}