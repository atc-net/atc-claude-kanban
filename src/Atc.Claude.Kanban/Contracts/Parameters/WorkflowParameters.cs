namespace Atc.Claude.Kanban.Contracts.Parameters;

/// <summary>
/// Route parameters identifying one workflow within a session.
/// </summary>
public sealed record WorkflowParameters(
    [FromRoute] string SessionId,
    [FromRoute] string WorkflowId);