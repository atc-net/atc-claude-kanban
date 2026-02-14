namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// A member of a Claude Code agent team.
/// </summary>
public sealed class TeamMember
{
    /// <summary>
    /// Gets or sets the member display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the agent identifier.
    /// </summary>
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    /// <summary>
    /// Gets or sets the agent type / role (e.g. "team-lead", "researcher").
    /// </summary>
    [JsonPropertyName("agentType")]
    public string? AgentType { get; set; }

    /// <summary>
    /// Gets or sets the model used by this agent.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }
}