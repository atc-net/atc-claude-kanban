namespace Atc.Claude.Kanban.Helpers;

/// <summary>
/// Shared path validation utilities.
/// </summary>
public static class PathHelper
{
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