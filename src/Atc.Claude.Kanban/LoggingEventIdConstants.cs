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

    public const int UpdateCheckStarted = 13001;
    public const int UpdateCheckCacheHit = 13002;
    public const int UpdateAvailable = 13003;
    public const int UpdateSucceeded = 13004;
    public const int UpdateFailed = 13005;
    public const int UpdateCheckFailed = 13006;
}