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

        group
            .MapGet("/{sessionId}/messages", GetMessagesForSession)
            .WithName("GetMessagesForSession")
            .WithDescription("Retrieve recent conversation messages from a session's JSONL transcript.")
            .WithSummary("Retrieve messages for a session.");
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

    [SuppressMessage("AsyncUsage", "AsyncFixer02:Long-running or blocking operations inside an async method", Justification = "In-memory LINQ on already-awaited collection.")]
    internal static async Task<Ok<MessagesResponse>> GetMessagesForSession(
        [FromServices] MessageService messageService,
        [AsParameters] GetMessagesParameters parameters,
        CancellationToken cancellationToken)
    {
        var limit = parameters.Limit ?? 15;

        if (!string.IsNullOrEmpty(parameters.Before))
        {
            var page = await messageService.GetMessagesPageAsync(
                parameters.SessionId,
                limit,
                parameters.Before,
                cancellationToken);
            return TypedResults.Ok(page);
        }

        // Fetch one extra to detect if there are more messages
        var messages = await messageService.GetRecentMessagesAsync(
            parameters.SessionId,
            limit + 1,
            cancellationToken);

        var hasMore = messages.Count > limit;
        IReadOnlyList<MessageEntry> result = hasMore
            ? messages.TakeLast(limit).ToList()
            : messages;

        return TypedResults.Ok(new MessagesResponse(result, hasMore));
    }
}