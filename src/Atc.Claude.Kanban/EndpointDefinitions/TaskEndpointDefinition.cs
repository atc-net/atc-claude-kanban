namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines the task CRUD API endpoints.
/// </summary>
public sealed class TaskEndpointDefinition : IEndpointDefinition
{
    internal const string ApiRouteBase = "/api/tasks";

    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        var group = app
            .MapGroup(ApiRouteBase)
            .WithTags("Tasks");

        group
            .MapGet("/all", GetAllTasks)
            .WithName("GetAllTasks")
            .WithDescription("Retrieve all tasks across all sessions.")
            .WithSummary("Retrieve all tasks.");

        group
            .MapPut("/{sessionId}/{taskId}", UpdateTask)
            .WithName("UpdateTask")
            .WithDescription("Update allowed fields on a task.")
            .WithSummary("Update a task.");

        group
            .MapPost("/{sessionId}/{taskId}/note", AddNote)
            .WithName("AddNoteToTask")
            .WithDescription("Append a note to a task description.")
            .WithSummary("Add a note to a task.");

        group
            .MapDelete("/{sessionId}/{taskId}", DeleteTask)
            .WithName("DeleteTask")
            .WithDescription("Delete a task if no other tasks depend on it.")
            .WithSummary("Delete a task.");
    }

    internal static async Task<Ok<IReadOnlyList<ClaudeTask>>> GetAllTasks(
        [FromServices] TaskService taskService,
        CancellationToken cancellationToken)
    {
        var tasks = await taskService.GetAllTasksAsync(cancellationToken);
        return TypedResults.Ok(tasks);
    }

    internal static async Task<Results<Ok<UpdateResult>, BadRequest<ErrorResult>, NotFound>> UpdateTask(
        [FromServices] TaskService taskService,
        [FromServices] JsonSerializerOptions jsonSerializerOptions,
        [AsParameters] TaskIdParameters parameters,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var updates = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(
            request.Body,
            jsonSerializerOptions,
            cancellationToken);

        if (updates is null)
        {
            return TypedResults.BadRequest(new ErrorResult("Invalid request body"));
        }

        var updatedTask = await taskService.UpdateTaskAsync(parameters.SessionId, parameters.TaskId, updates, cancellationToken);
        return updatedTask is not null
            ? TypedResults.Ok(new UpdateResult(true, updatedTask))
            : TypedResults.NotFound();
    }

    internal static async Task<Results<Ok<AddNoteResult>, BadRequest<ErrorResult>, NotFound>> AddNote(
        [FromServices] TaskService taskService,
        [FromServices] JsonSerializerOptions jsonSerializerOptions,
        [AsParameters] TaskIdParameters parameters,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
            request.Body,
            jsonSerializerOptions,
            cancellationToken);

        if (body is null || !body.TryGetValue("note", out var note) || string.IsNullOrWhiteSpace(note))
        {
            return TypedResults.BadRequest(new ErrorResult("Note cannot be empty"));
        }

        var updatedTask = await taskService.AddNoteAsync(parameters.SessionId, parameters.TaskId, note, cancellationToken);
        return updatedTask is not null
            ? TypedResults.Ok(new AddNoteResult(true, updatedTask))
            : TypedResults.NotFound();
    }

    internal static async Task<Results<Ok<DeleteResult>, NotFound, BadRequest<DeleteErrorResult>>> DeleteTask(
        [FromServices] TaskService taskService,
        [AsParameters] TaskIdParameters parameters,
        CancellationToken cancellationToken)
    {
        var (success, error, blockedTasks) = await taskService.DeleteTaskAsync(
            parameters.SessionId,
            parameters.TaskId,
            cancellationToken);

        if (success)
        {
            return TypedResults.Ok(new DeleteResult(true, parameters.TaskId));
        }

        // blockedTasks is null when task not found, non-null when blocked by dependencies
        return blockedTasks is null
            ? TypedResults.NotFound()
            : TypedResults.BadRequest(new DeleteErrorResult(error ?? "Delete failed", blockedTasks));
    }
}