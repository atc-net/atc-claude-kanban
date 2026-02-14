namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines the subagent-related API endpoints.
/// </summary>
public sealed class SubagentEndpointDefinition : IEndpointDefinition
{
    internal const string ApiRouteBase = "/api/sessions/{sessionId}/subagents";

    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        var group = app
            .MapGroup(ApiRouteBase)
            .WithTags("Subagents");

        group
            .MapGet("/", GetSubagents)
            .WithName("GetSubagents")
            .WithDescription("Retrieve subagent information for a session, parsed from JSONL transcript files.")
            .WithSummary("Retrieve subagents for a session.");
    }

    internal static async Task<Ok<IReadOnlyList<SubagentInfo>>> GetSubagents(
        [FromServices] SubagentService subagentService,
        [AsParameters] SessionIdParameters parameters,
        CancellationToken cancellationToken)
    {
        var subagents = await subagentService.GetSubagentsForSessionAsync(parameters.SessionId, cancellationToken);
        return TypedResults.Ok(subagents);
    }
}