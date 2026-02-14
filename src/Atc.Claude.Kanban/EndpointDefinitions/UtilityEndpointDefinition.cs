namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines utility endpoints such as opening folders in the system file explorer.
/// </summary>
public sealed class UtilityEndpointDefinition : IEndpointDefinition
{
    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        app
            .MapPost("/api/open-folder", OpenFolder)
            .WithTags("Utility")
            .WithName("OpenFolder")
            .WithDescription("Open a folder in the system file explorer.")
            .WithSummary("Open folder in explorer.");
    }

    internal static Results<Ok, BadRequest, StatusCodeHttpResult> OpenFolder(
        [FromBody] OpenFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path) || !Directory.Exists(request.Path))
        {
            return TypedResults.BadRequest();
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = request.Path,
                UseShellExecute = true,
            });

            return TypedResults.Ok();
        }
        catch (InvalidOperationException)
        {
            return TypedResults.StatusCode(500);
        }
    }
}