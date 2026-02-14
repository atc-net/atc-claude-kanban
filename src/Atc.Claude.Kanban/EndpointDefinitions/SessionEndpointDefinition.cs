namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines the session-related API endpoints.
/// </summary>
public sealed class SessionEndpointDefinition : IEndpointDefinition
{
    internal const string ApiRouteBase = "/api/sessions";

    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        var group = app
            .MapGroup(ApiRouteBase)
            .WithTags("Sessions");

        group
            .MapGet("/", GetSessions)
            .WithName("GetSessions")
            .WithDescription("Retrieve all sessions ordered by last modification time.")
            .WithSummary("Retrieve all sessions.");

        group
            .MapGet("/{sessionId}", GetTasksForSession)
            .WithName("GetTasksForSession")
            .WithDescription("Retrieve all tasks for a specific session.")
            .WithSummary("Retrieve tasks for a session.");

        group
            .MapGet("/{sessionId}/agents", GetAgentTasksForSession)
            .WithName("GetAgentTasksForSession")
            .WithDescription("Retrieve internal agent lifecycle tasks for a session.")
            .WithSummary("Retrieve agent tasks for a session.");
    }

    internal static async Task<Ok<IReadOnlyList<SessionInfo>>> GetSessions(
        [FromServices] SessionService sessionService,
        [AsParameters] GetSessionsParameters parameters,
        CancellationToken cancellationToken)
    {
        var limit = parameters.Limit ?? 20;
        var sessions = await sessionService.GetSessionsAsync(limit, cancellationToken);
        return TypedResults.Ok(sessions);
    }

    internal static async Task<Ok<IReadOnlyList<ClaudeTask>>> GetTasksForSession(
        [FromServices] TaskService taskService,
        [AsParameters] SessionIdParameters parameters,
        CancellationToken cancellationToken)
    {
        var tasks = await taskService.GetTasksForSessionAsync(parameters.SessionId, cancellationToken);
        return TypedResults.Ok(tasks);
    }

    internal static async Task<Ok<IReadOnlyList<ClaudeTask>>> GetAgentTasksForSession(
        [FromServices] TaskService taskService,
        [AsParameters] SessionIdParameters parameters,
        CancellationToken cancellationToken)
    {
        var agentTasks = await taskService.GetAgentTasksForSessionAsync(parameters.SessionId, cancellationToken);
        return TypedResults.Ok(agentTasks);
    }
}