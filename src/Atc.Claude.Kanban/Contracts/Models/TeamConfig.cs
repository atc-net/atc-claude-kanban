namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Team configuration as stored in ~/.claude/teams/{teamName}/config.json.
/// </summary>
public sealed class TeamConfig
{
    /// <summary>
    /// Gets or sets the team name.
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
    [JsonPropertyName("lead_agent_id")]
    public string? LeadAgentId { get; set; }

    /// <summary>
    /// Gets or sets the lead session identifier used to inherit metadata.
    /// </summary>
    [JsonPropertyName("lead_session_id")]
    public string? LeadSessionId { get; set; }

    /// <summary>
    /// Gets or sets the working directory for the team.
    /// </summary>
    [JsonPropertyName("working_dir")]
    public string? WorkingDir { get; set; }

    /// <summary>
    /// Gets or sets the team creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}