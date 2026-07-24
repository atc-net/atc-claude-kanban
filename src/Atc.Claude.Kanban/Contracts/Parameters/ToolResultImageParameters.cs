namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route parameters for fetching a base64 image embedded in a tool_result block.
/// </summary>
public sealed record ToolResultImageParameters(
    [FromRoute] string SessionId,
    [FromRoute] string ToolUseId,
    [FromRoute] int ImageIndex);