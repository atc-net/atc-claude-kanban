namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Reads team configurations from ~/.claude/teams/{teamName}/config.json.
/// Uses <see cref="IMemoryCache"/> with 5-second TTL.
/// </summary>
public sealed class TeamService
{
    private readonly string claudeDir;
    private readonly IMemoryCache cache;
    private readonly JsonSerializerOptions jsonSerializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="TeamService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="cache">Memory cache for team configs.</param>
    /// <param name="jsonSerializerOptions">The shared JSON serializer options.</param>
    public TeamService(
        string claudeDir,
        IMemoryCache cache,
        JsonSerializerOptions jsonSerializerOptions)
    {
        this.claudeDir = claudeDir;
        this.cache = cache;
        this.jsonSerializerOptions = jsonSerializerOptions;
    }

    /// <summary>
    /// Returns the team configuration for the given team name, or null if not found.
    /// </summary>
    /// <param name="teamName">The name of the team to look up.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The team configuration, or <see langword="null"/> if not found.</returns>
    public async Task<TeamConfig?> GetTeamConfigAsync(
        string teamName,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"team:{teamName}";
        if (cache.TryGetValue(cacheKey, out TeamConfig? cached))
        {
            return cached;
        }

        var configFile = Path.Combine(claudeDir, "teams", teamName, "config.json");
        if (!File.Exists(configFile))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configFile, cancellationToken);
            var config = JsonSerializer.Deserialize<TeamConfig>(json, jsonSerializerOptions);
            cache.Set(cacheKey, config, TimeSpan.FromSeconds(5));
            return config;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}