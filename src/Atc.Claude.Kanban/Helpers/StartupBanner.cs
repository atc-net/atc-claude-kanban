namespace Atc.Claude.Kanban.Helpers;

/// <summary>
/// Prints a colored ASCII art startup banner to the console.
/// </summary>
internal static class StartupBanner
{
    private const string Cyan = "\u001b[36m";
    private const string BoldWhite = "\u001b[1;37m";
    private const string Dim = "\u001b[90m";
    private const string BrightWhite = "\u001b[97m";
    private const string Reset = "\u001b[0m";

    /// <summary>
    /// Writes the ATC Claude Kanban startup banner with ANSI colors.
    /// </summary>
    /// <param name="url">The dashboard URL.</param>
    /// <param name="claudeDir">The watched Claude directory path.</param>
    /// <param name="version">The application version string.</param>
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "ASCII art banner is not localizable")]
    internal static void Print(
        string url,
        string claudeDir,
        string version)
    {
        var displayDir = CollapseHomePath(claudeDir);

        System.Console.WriteLine();
        System.Console.WriteLine($"{Cyan}   █████╗ ████████╗ ██████╗{Reset}");
        System.Console.WriteLine($"{Cyan}  ██╔══██╗╚══██╔══╝██╔════╝{Reset}");
        System.Console.WriteLine($"{Cyan}  ███████║   ██║   ██║{Reset}");
        System.Console.WriteLine($"{Cyan}  ██╔══██║   ██║   ██║{Reset}");
        System.Console.WriteLine($"{Cyan}  ██║  ██║   ██║   ╚██████╗{Reset}");
        System.Console.WriteLine($"{Cyan}  ╚═╝  ╚═╝   ╚═╝    ╚═════╝  {BoldWhite}Claude Kanban{Reset}");
        System.Console.WriteLine();
        System.Console.WriteLine($"  🌐 {Dim}Dashboard{Reset}  {BrightWhite}{url}{Reset}");
        System.Console.WriteLine($"  📂 {Dim}Watching{Reset}   {BrightWhite}{displayDir}{Reset}");
        System.Console.WriteLine($"  👁  {Dim}Watchers{Reset}   {BrightWhite}tasks{Reset} {Dim}·{Reset} {BrightWhite}teams{Reset} {Dim}·{Reset} {BrightWhite}projects{Reset} {Dim}·{Reset} {BrightWhite}plans{Reset}");
        System.Console.WriteLine($"  🏷  {Dim}Version{Reset}    {BrightWhite}{version}{Reset}");
        System.Console.WriteLine();
    }

    private static string CollapseHomePath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return "~" + path[home.Length..].Replace('\\', '/');
    }
}