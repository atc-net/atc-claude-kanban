namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines utility endpoints such as opening folders in the system file explorer.
/// </summary>
public sealed class UtilityEndpointDefinition : IEndpointDefinition
{
    private const string EditorCommand = "code";

    private const string InvalidPathMessage = "Path must be absolute and free of control characters.";

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

    internal static Results<Ok, BadRequest<ErrorResult>, InternalServerError<ErrorResult>> OpenFolder(
        [FromBody] OpenFolderRequest request)
    {
        if (!PathHelper.IsSafeLaunchPath(request.Path))
        {
            return TypedResults.BadRequest(new ErrorResult(InvalidPathMessage));
        }

        if (!Directory.Exists(request.Path))
        {
            return TypedResults.BadRequest(new ErrorResult("Folder does not exist."));
        }

        // The Directory.Exists guard above keeps this from shell-executing an arbitrary file.
        return Launch(
            new ProcessStartInfo
            {
                FileName = request.Path,
                UseShellExecute = true,
            },
            "Could not open the folder.");
    }

    [SuppressMessage("Security", "S4036:Use an absolute path for this command", Justification = "The VS Code 'code' launcher is resolved from PATH intentionally; its install location is machine- and OS-specific and cannot be hard-coded.")]
    internal static Results<Ok, BadRequest<ErrorResult>, InternalServerError<ErrorResult>> OpenInEditor(
        [FromBody] OpenInEditorRequest request)
    {
        if (!PathHelper.IsSafeLaunchPath(request.Path))
        {
            return TypedResults.BadRequest(new ErrorResult(InvalidPathMessage));
        }

        if (!File.Exists(request.Path) &&
            !Directory.Exists(request.Path))
        {
            return TypedResults.BadRequest(new ErrorResult("File does not exist."));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = EditorCommand,
            UseShellExecute = true,
        };

        AddEditorArguments(startInfo, request.Path, request.Line);

        return Launch(
            startInfo,
            $"Could not launch the editor. Make sure the '{EditorCommand}' command is on your PATH.");
    }

    /// <summary>
    /// Appends the target path as discrete arguments so the runtime quotes them.
    /// </summary>
    /// <remarks>
    /// Building a single argument string by concatenation lets a path containing a
    /// quote splice extra arguments into the editor invocation. Populating
    /// <see cref="ProcessStartInfo.ArgumentList"/> leaves the quoting to the runtime,
    /// so the path is always passed as exactly one argument.
    /// </remarks>
    /// <param name="startInfo">The start info to populate.</param>
    /// <param name="path">The file or folder to open.</param>
    /// <param name="line">The optional one-based line number to jump to.</param>
    internal static void AddEditorArguments(
        ProcessStartInfo startInfo,
        string path,
        int? line)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (line is > 0)
        {
            startInfo.ArgumentList.Add("-g");
            startInfo.ArgumentList.Add(
                string.Create(CultureInfo.InvariantCulture, $"{path}:{line}"));
        }
        else
        {
            startInfo.ArgumentList.Add(path);
        }
    }

    private static Results<Ok, BadRequest<ErrorResult>, InternalServerError<ErrorResult>> Launch(
        ProcessStartInfo startInfo,
        string failureMessage)
    {
        try
        {
            using var process = Process.Start(startInfo);

            return TypedResults.Ok();
        }
        catch (Win32Exception ex)
        {
            return TypedResults.InternalServerError(new ErrorResult($"{failureMessage} {ex.Message}"));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.InternalServerError(new ErrorResult($"{failureMessage} {ex.Message}"));
        }
    }
}