namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Reads plan markdown files from ~/.claude/plans/.
/// </summary>
public sealed class PlanService
{
    private readonly string claudeDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    public PlanService(string claudeDir)
        => this.claudeDir = claudeDir;

    /// <summary>
    /// Returns the plan markdown content for a slug, or null if no plan exists.
    /// </summary>
    /// <param name="slug">The plan slug (human-readable session name).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The plan markdown content, or <see langword="null"/> if no plan exists.</returns>
    public async Task<string?> GetPlanForSessionAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetPlanFilePath(slug);
        return filePath is not null ? await ReadPlanFileAsync(filePath, cancellationToken) : null;
    }

    /// <summary>
    /// Returns the absolute file path of a plan for a slug, or null if no plan exists.
    /// Validates that the resolved path stays within the plans directory.
    /// </summary>
    /// <param name="slug">The plan slug (human-readable session name).</param>
    /// <returns>The absolute file path of the plan, or <see langword="null"/> if no plan exists.</returns>
    public string? GetPlanFilePath(string slug)
    {
        var plansDir = Path.GetFullPath(Path.Combine(claudeDir, "plans"));
        if (!Directory.Exists(plansDir))
        {
            return null;
        }

        var exactFile = Path.GetFullPath(Path.Combine(plansDir, $"{slug}.md"));
        if (PathHelper.IsUnderDirectory(exactFile, plansDir) && File.Exists(exactFile))
        {
            return exactFile;
        }

        // Prefix match for agent-specific plan variants (e.g., "my-slug-agent-1.md")
        var files = Directory.GetFiles(plansDir, "*.md")
            .Where(f => Path.GetFileName(f).StartsWith(slug, StringComparison.Ordinal))
            .Where(f => PathHelper.IsUnderDirectory(Path.GetFullPath(f), plansDir))
            .ToArray();

        return files.Length > 0
            ? files.OrderByDescending(File.GetLastWriteTimeUtc).First()
            : null;
    }

    private static async Task<string?> ReadPlanFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
    }
}