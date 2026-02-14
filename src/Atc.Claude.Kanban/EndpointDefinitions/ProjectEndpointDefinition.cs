namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines the projects listing endpoint.
/// </summary>
public sealed class ProjectEndpointDefinition : IEndpointDefinition
{
    internal const string ApiRouteBase = "/api/projects";

    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        var group = app
            .MapGroup(ApiRouteBase)
            .WithTags("Projects");

        group
            .MapGet("/", GetProjects)
            .WithName("GetProjects")
            .WithDescription("Retrieve distinct project paths with their most recent modification times.")
            .WithSummary("Retrieve all projects.");
    }

    internal static async Task<Ok<IReadOnlyList<ProjectInfo>>> GetProjects(
        [FromServices] SessionService sessionService,
        CancellationToken cancellationToken)
    {
        var projects = await sessionService.GetProjectsAsync(cancellationToken);
        return TypedResults.Ok(projects);
    }
}