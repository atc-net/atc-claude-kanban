namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Source-generated logger messages for <see cref="ClaudeDirectoryWatcher"/>.
/// </summary>
[SuppressMessage("Design", "MA0048:File name must match type name", Justification = "OK - By Design")]
public sealed partial class ClaudeDirectoryWatcher
{
    [LoggerMessage(
        EventId = LoggingEventIdConstants.WatcherRegistered,
        Level = LogLevel.Information,
        Message = "Watching directory '{Directory}'.")]
    private partial void LogWatching(string directory);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.WatcherDirectoryCreateFailed,
        Level = LogLevel.Warning,
        Message = "Could not create watch directory '{Directory}'.")]
    private partial void LogCouldNotCreateDirectory(
        Exception exception,
        string directory);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.WatcherCreateFailed,
        Level = LogLevel.Warning,
        Message = "Failed to create file system watcher for '{Directory}'.")]
    private partial void LogWatcherCreateFailed(
        Exception exception,
        string directory);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.WatcherError,
        Level = LogLevel.Error,
        Message = "FileSystemWatcher error for '{Directory}'.")]
    private partial void LogWatcherError(
        Exception exception,
        string directory);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.FileEventProcessingError,
        Level = LogLevel.Error,
        Message = "Error processing file change event.")]
    private partial void LogProcessingError(Exception exception);
}