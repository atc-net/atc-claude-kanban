namespace Atc.Claude.Kanban.Helpers;

/// <summary>
/// Shared path validation and display utilities.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Replaces the user's home directory prefix with <c>~</c> and normalizes separators to forward slashes.
    /// </summary>
    /// <param name="path">The absolute path to shorten.</param>
    /// <returns>The display-friendly path.</returns>
    public static string CollapseHomePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return "~" + path[home.Length..].Replace('\\', '/');
    }

    /// <summary>
    /// Validates that a resolved file path is within the expected directory.
    /// Prevents path traversal attacks via crafted identifiers.
    /// </summary>
    /// <param name="filePath">The absolute file path to check.</param>
    /// <param name="directory">The directory that should contain the file.</param>
    /// <returns><see langword="true"/> if <paramref name="filePath"/> is under <paramref name="directory"/>.</returns>
    public static bool IsUnderDirectory(
        string filePath,
        string directory)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(directory);

        var normalizedDir = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

        return filePath.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase);
    }
}