namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Team configuration as stored in ~/.claude/teams/{teamName}/config.json.
/// </summary>
public sealed class TeamConfig
{
    /// <summary>
    /// Gets or sets the team name (from <c>team_name</c> field in older configs).
    /// </summary>
    [JsonPropertyName("team_name")]
    public string? TeamName { get; set; }

    /// <summary>
    /// Gets or sets the team display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the team description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the team members.
    /// </summary>
    [JsonPropertyName("members")]
    public IReadOnlyList<TeamMember>? Members { get; set; }

    /// <summary>
    /// Gets or sets the lead agent identifier.
    /// </summary>
    [JsonPropertyName("leadAgentId")]
    public string? LeadAgentId { get; set; }

    /// <summary>
    /// Gets or sets the lead session identifier used to inherit metadata.
    /// </summary>
    [JsonPropertyName("leadSessionId")]
    public string? LeadSessionId { get; set; }

    /// <summary>
    /// Gets or sets the working directory for the team.
    /// </summary>
    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; set; }

    /// <summary>
    /// Gets or sets the team creation timestamp as Unix milliseconds.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public long? CreatedAt { get; set; }
}