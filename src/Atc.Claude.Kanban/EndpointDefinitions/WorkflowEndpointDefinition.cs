namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines the workflow-related API endpoints.
/// </summary>
public sealed class WorkflowEndpointDefinition : IEndpointDefinition
{
    internal const string ApiRouteBase = "/api/sessions/{sessionId}/workflows";

    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        var group = app
            .MapGroup(ApiRouteBase)
            .WithTags("Workflows");

        group
            .MapGet("/", GetWorkflows)
            .WithName("GetWorkflows")
            .WithDescription("Retrieve the Workflow-tool scripts belonging to a session, newest first.")
            .WithSummary("Retrieve workflows for a session.");

        group
            .MapGet("/{workflowId}", GetWorkflowSource)
            .WithName("GetWorkflowSource")
            .WithDescription("Retrieve a workflow script's source.")
            .WithSummary("Retrieve a workflow script.");

        group
            .MapGet("/{workflowId}/run", GetWorkflowRun)
            .WithName("GetWorkflowRun")
            .WithDescription("Retrieve a workflow run's declared phases and agent roster.")
            .WithSummary("Retrieve a workflow run.");

        group
            .MapPost("/{workflowId}/open", OpenWorkflowInEditor)
            .WithName("OpenWorkflowInEditor")
            .WithDescription("Open a workflow script in the system default editor.")
            .WithSummary("Open a workflow script in editor.");
    }

    internal static async Task<Ok<WorkflowListResponse>> GetWorkflows(
        [FromServices] WorkflowService workflowService,
        [AsParameters] SessionIdParameters parameters,
        CancellationToken cancellationToken)
    {
        var workflows = await workflowService.GetWorkflowsForSessionAsync(parameters.SessionId, cancellationToken);
        return TypedResults.Ok(new WorkflowListResponse(workflows));
    }

    internal static async Task<Results<Ok<WorkflowSourceResponse>, NotFound>> GetWorkflowSource(
        [FromServices] WorkflowService workflowService,
        [AsParameters] WorkflowParameters parameters,
        CancellationToken cancellationToken)
    {
        var source = await workflowService.GetWorkflowSourceAsync(
            parameters.SessionId,
            parameters.WorkflowId,
            cancellationToken);

        return source is not null
            ? TypedResults.Ok(source)
            : TypedResults.NotFound();
    }

    internal static async Task<Results<Ok<WorkflowRunResponse>, NotFound>> GetWorkflowRun(
        [FromServices] WorkflowService workflowService,
        [AsParameters] WorkflowParameters parameters,
        CancellationToken cancellationToken)
    {
        var run = await workflowService.GetWorkflowRunAsync(
            parameters.SessionId,
            parameters.WorkflowId,
            cancellationToken);

        return run is not null
            ? TypedResults.Ok(run)
            : TypedResults.NotFound();
    }

    [SuppressMessage("Security", "S4036:Use an absolute path for this command", Justification = "The VS Code 'code' launcher is resolved from PATH intentionally; its install location is machine- and OS-specific and cannot be hard-coded.")]
    internal static Results<Ok, NotFound, StatusCodeHttpResult> OpenWorkflowInEditor(
        [FromServices] WorkflowService workflowService,
        [AsParameters] WorkflowParameters parameters)
    {
        var filePath = workflowService.GetWorkflowScriptPath(parameters.SessionId, parameters.WorkflowId);
        if (filePath is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            // Launch an editor rather than the shell handler: a .js file usually has no
            // sensible file association, so shell-executing it prompts for an application.
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("EDITOR") is { Length: > 0 } editor ? editor : "code",
                Arguments = $"\"{filePath}\"",
                UseShellExecute = true,
            });

            return TypedResults.Ok();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return TypedResults.StatusCode(500);
        }
        catch (InvalidOperationException)
        {
            return TypedResults.StatusCode(500);
        }
    }
}