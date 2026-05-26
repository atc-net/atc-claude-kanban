namespace Atc.Claude.Kanban.Contracts.Models;

/// <summary>
/// Describes a base64 image attached to a user message. Only metadata is sent with
/// the message list; the image bytes are fetched lazily by block index to keep the
/// messages payload small.
/// </summary>
public sealed class MessageImage
{
    /// <summary>
    /// Gets or sets the zero-based index of the image block within the user
    /// message content array, used to fetch the image bytes lazily.
    /// </summary>
    [JsonPropertyName("blockIndex")]
    public int BlockIndex { get; set; }

    /// <summary>
    /// Gets or sets the image media type (e.g. "image/png").
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "image/png";
}