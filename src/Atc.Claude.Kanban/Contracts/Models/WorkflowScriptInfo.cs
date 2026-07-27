namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// A Workflow-tool script persisted for a session at
/// projects/{hash}/{sessionId}/workflows/scripts/{name}-{runId}.js.
/// </summary>
public sealed class WorkflowScriptInfo
{
    /// <summary>
    /// Gets or sets the workflow run identifier taken from the filename suffix (e.g. "wf_ac6c775f-614").
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the workflow name taken from the filename, or from the script's meta block
    /// when it has been parsed.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the one-line description declared in the script's meta block.
    /// </summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the script file's last modification time.
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public DateTime ModifiedAt { get; set; }
}