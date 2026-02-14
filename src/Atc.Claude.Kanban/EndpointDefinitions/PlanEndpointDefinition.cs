namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines the plan-related API endpoints.
/// </summary>
public sealed class PlanEndpointDefinition : IEndpointDefinition
{
    internal const string ApiRouteBase = "/api/plans/{slug}";

    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        var group = app
            .MapGroup(ApiRouteBase)
            .WithTags("Plans");

        group
            .MapGet("/", GetPlan)
            .WithName("GetPlan")
            .WithDescription("Retrieve plan markdown content by slug.")
            .WithSummary("Retrieve a plan.");

        group
            .MapPost("/open", OpenPlanInEditor)
            .WithName("OpenPlanInEditor")
            .WithDescription("Open the plan file in the system default editor.")
            .WithSummary("Open a plan in editor.");
    }

    internal static async Task<Results<Ok<PlanContent>, NotFound>> GetPlan(
        [FromServices] PlanService planService,
        [AsParameters] SlugParameters parameters,
        CancellationToken cancellationToken)
    {
        var content = await planService.GetPlanForSessionAsync(parameters.Slug, cancellationToken);

        return content is not null
            ? TypedResults.Ok(new PlanContent(content, parameters.Slug))
            : TypedResults.NotFound();
    }

    internal static Results<Ok, NotFound, StatusCodeHttpResult> OpenPlanInEditor(
        [FromServices] PlanService planService,
        [AsParameters] SlugParameters parameters)
    {
        var filePath = planService.GetPlanFilePath(parameters.Slug);
        if (filePath is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            var editor = Environment.GetEnvironmentVariable("EDITOR");
            if (!string.IsNullOrEmpty(editor))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = editor,
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                });
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true,
                });
            }

            return TypedResults.Ok();
        }
        catch (InvalidOperationException)
        {
            return TypedResults.StatusCode(500);
        }
    }
}