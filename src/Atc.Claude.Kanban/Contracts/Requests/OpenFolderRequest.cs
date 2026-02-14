namespace Atc.Claude.Kanban.Contracts.Requests;

/// <summary>
/// Request body for opening a folder in the system file explorer.
/// </summary>
public sealed record OpenFolderRequest(string Path);