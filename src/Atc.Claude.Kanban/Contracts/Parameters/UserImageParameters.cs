namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route parameters for fetching a base64 image attached to a user message.
/// </summary>
public sealed record UserImageParameters(
    [FromRoute] string SessionId,
    [FromRoute] string MessageUuid,
    [FromRoute] int BlockIndex);