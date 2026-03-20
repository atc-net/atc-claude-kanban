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

        group
            .MapGet("/{agentId}/messages", GetSubagentMessages)
            .WithName("GetSubagentMessages")
            .WithDescription("Retrieve recent conversation messages from a subagent's JSONL transcript.")
            .WithSummary("Retrieve messages for a subagent.");
    }

    internal static async Task<Ok<IReadOnlyList<SubagentInfo>>> GetSubagents(
        [FromServices] SubagentService subagentService,
        [FromServices] TeamService teamService,
        [AsParameters] SessionIdParameters parameters,
        CancellationToken cancellationToken)
    {
        var subagents = await subagentService.GetSubagentsForSessionAsync(parameters.SessionId, cancellationToken);

        // For team sessions, subagent files live under the lead session's UUID
        if (subagents.Count == 0)
        {
            var teamConfig = await teamService.GetTeamConfigAsync(parameters.SessionId, cancellationToken);
            if (teamConfig?.LeadSessionId is not null)
            {
                subagents = await subagentService.GetSubagentsForSessionAsync(teamConfig.LeadSessionId, cancellationToken);
            }
        }

        return TypedResults.Ok(subagents);
    }

    internal static async Task<Ok<IReadOnlyList<MessageEntry>>> GetSubagentMessages(
        [FromServices] MessageService messageService,
        [AsParameters] GetSubagentMessagesParameters parameters,
        CancellationToken cancellationToken)
    {
        var limit = parameters.Limit ?? 50;

        var messages = await messageService.GetSubagentMessagesAsync(
            parameters.SessionId,
            parameters.AgentId,
            limit,
            cancellationToken);

        return TypedResults.Ok(messages);
    }
}