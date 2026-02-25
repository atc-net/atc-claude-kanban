namespace Atc.Claude.Kanban;

/// <summary>
/// Parsed command-line options for the Kanban dashboard.
/// </summary>
internal sealed record CliOptions
{
    /// <summary>
    /// Gets the TCP port to listen on.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Gets a value indicating whether the port was explicitly specified via <c>--port</c>.
    /// </summary>
    public required bool ExplicitPort { get; init; }

    /// <summary>
    /// Gets a value indicating whether to open the dashboard in the default browser on startup.
    /// </summary>
    public required bool OpenBrowser { get; init; }

    /// <summary>
    /// Gets the path to the <c>~/.claude</c> directory to watch.
    /// </summary>
    public required string ClaudeDir { get; init; }

    /// <summary>
    /// Gets a value indicating whether to skip the NuGet update check on startup.
    /// </summary>
    public required bool NoUpdateCheck { get; init; }
}