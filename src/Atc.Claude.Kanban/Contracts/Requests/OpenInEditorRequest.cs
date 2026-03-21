namespace Atc.Claude.Kanban.Contracts.Requests;

/// <summary>
/// Request body for opening a file in the default code editor.
/// </summary>
public sealed record OpenInEditorRequest(
    string Path,
    int? Line);