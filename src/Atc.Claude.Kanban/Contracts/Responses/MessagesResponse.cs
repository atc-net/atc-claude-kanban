namespace Atc.Claude.Kanban.Contracts.Responses;

/// <summary>
/// Response record for paginated message queries.
/// </summary>
public sealed record MessagesResponse(
    [property: JsonPropertyName("messages")] IReadOnlyList<MessageEntry> Messages,
    [property: JsonPropertyName("hasMore")] bool HasMore);