namespace Atc.Claude.Kanban.UpdateCheck.Services;

/// <summary>
/// Source-generated logger messages for <see cref="UpdateCheckService"/>.
/// </summary>
[SuppressMessage("Design", "MA0048:File name must match type name", Justification = "OK - By Design")]
public sealed partial class UpdateCheckService
{
    [LoggerMessage(
        EventId = LoggingEventIdConstants.UpdateCheckStarted,
        Level = LogLevel.Debug,
        Message = "Checking NuGet for newer version of atc-claude-kanban.")]
    private partial void LogUpdateCheckStarted();

    [LoggerMessage(
        EventId = LoggingEventIdConstants.UpdateCheckCacheHit,
        Level = LogLevel.Debug,
        Message = "Update check cache is fresh (last check: {LastCheck}), skipping NuGet query.")]
    private partial void LogUpdateCheckCacheHit(DateTimeOffset lastCheck);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.UpdateAvailable,
        Level = LogLevel.Debug,
        Message = "Update available: {CurrentVersion} -> {LatestVersion}.")]
    private partial void LogUpdateAvailable(string currentVersion, string latestVersion);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.UpdateSucceeded,
        Level = LogLevel.Debug,
        Message = "Auto-update to {LatestVersion} succeeded.")]
    private partial void LogUpdateSucceeded(string latestVersion);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.UpdateFailed,
        Level = LogLevel.Warning,
        Message = "Auto-update failed or timed out.")]
    private partial void LogUpdateFailed(Exception exception);

    [LoggerMessage(
        EventId = LoggingEventIdConstants.UpdateCheckFailed,
        Level = LogLevel.Warning,
        Message = "Update check failed.")]
    private partial void LogUpdateCheckFailed(Exception exception);
}