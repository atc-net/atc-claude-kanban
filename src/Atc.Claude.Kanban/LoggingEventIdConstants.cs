namespace Atc.Claude.Kanban;

/// <summary>
/// Constants for structured logging event identifiers used throughout the application.
/// </summary>
internal static class LoggingEventIdConstants
{
    public const int WatcherRegistered = 11001;
    public const int WatcherDirectoryCreateFailed = 11002;
    public const int WatcherCreateFailed = 11003;
    public const int WatcherError = 11004;

    public const int FileEventProcessingError = 12000;
}