namespace Atc.Claude.Kanban.Contracts.Events;

/// <summary>
/// Represents a file system change event detected by the <see cref="Services.ClaudeDirectoryWatcher"/>.
/// </summary>
public sealed class FileChangeEvent
{
    /// <summary>
    /// Gets or sets the absolute path of the changed file.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Gets or sets the category of the change (tasks, teams, projects, or plans).
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets or sets the event type mapped from <see cref="System.IO.WatcherChangeTypes"/>
    /// (e.g. "add", "change", "unlink").
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Gets or sets the file name of the changed file (e.g. "2.json").
    /// </summary>
    public required string FileName { get; init; }
}