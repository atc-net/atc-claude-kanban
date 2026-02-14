namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines the team configuration API endpoint.
/// </summary>
public sealed class TeamEndpointDefinition : IEndpointDefinition
{
    internal const string ApiRouteBase = "/api/teams";

    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        var group = app
            .MapGroup(ApiRouteBase)
            .WithTags("Teams");

        group
            .MapGet("/{name}", GetTeamConfig)
            .WithName("GetTeamConfig")
            .WithDescription("Retrieve team configuration including members and roles.")
            .WithSummary("Retrieve a team configuration.");
    }

    internal static async Task<Results<Ok<TeamConfig>, NotFound>> GetTeamConfig(
        [FromServices] TeamService teamService,
        [AsParameters] TeamNameParameters parameters,
        CancellationToken cancellationToken)
    {
        var config = await teamService.GetTeamConfigAsync(parameters.Name, cancellationToken);
        return config is not null
            ? TypedResults.Ok(config)
            : TypedResults.NotFound();
    }
}