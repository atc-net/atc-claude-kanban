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

        app
            .MapPost("/api/open-in-editor", OpenInEditor)
            .WithTags("Utility")
            .WithName("OpenInEditor")
            .WithDescription("Open a file in the default code editor (VS Code).")
            .WithSummary("Open file in editor.");
    }

    internal static Results<Ok, BadRequest, StatusCodeHttpResult> OpenFolder(
        [FromBody] OpenFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path) ||
            !Directory.Exists(request.Path))
        {
            return TypedResults.BadRequest();
        }

        try
        {
            Process.Start(new ProcessStartInfo
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

    [SuppressMessage("Security", "S4036:Use an absolute path for this command", Justification = "The VS Code 'code' launcher is resolved from PATH intentionally; its install location is machine- and OS-specific and cannot be hard-coded.")]
    internal static Results<Ok, BadRequest, StatusCodeHttpResult> OpenInEditor(
        [FromBody] OpenInEditorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return TypedResults.BadRequest();
        }

        try
        {
            var args = request.Line is > 0
                ? $"-g \"{request.Path}\":{request.Line}"
                : $"\"{request.Path}\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = "code",
                Arguments = args,
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