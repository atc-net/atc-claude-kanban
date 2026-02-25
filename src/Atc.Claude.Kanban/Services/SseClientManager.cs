namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Manages Server-Sent Events client connections.
/// Each connected browser gets a bounded <see cref="Channel{T}"/> that receives
/// <see cref="SseNotification"/> messages broadcast by the <see cref="ClaudeDirectoryWatcher"/>.
/// </summary>
public sealed class SseClientManager
{
    private readonly ConcurrentDictionary<string, Channel<SseNotification>> clients = new(StringComparer.Ordinal);

    private volatile SseNotification? pendingVersionUpdate;

    /// <summary>
    /// Gets the number of currently connected SSE clients.
    /// </summary>
    public int ClientCount => clients.Count;

    /// <summary>
    /// Registers a new SSE client and returns its unique ID and notification channel.
    /// If a version-update notification was broadcast before this client connected,
    /// it is immediately written to the new client's channel.
    /// </summary>
    /// <returns>A tuple containing the client ID and the notification channel.</returns>
    public (string ClientId, Channel<SseNotification> Channel) AddClient()
    {
        var clientId = Guid.NewGuid().ToString("N");

        var channel = Channel.CreateBounded<SseNotification>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        clients.TryAdd(clientId, channel);

        // Replay pending version-update for late-connecting clients
        var pending = pendingVersionUpdate;
        if (pending is not null)
        {
            channel.Writer.TryWrite(pending);
        }

        return (clientId, channel);
    }

    /// <summary>
    /// Removes a disconnected SSE client and completes its channel.
    /// </summary>
    /// <param name="clientId">The unique identifier of the client to remove.</param>
    public void RemoveClient(string clientId)
    {
        if (clients.TryRemove(clientId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Sends a notification to every connected SSE client.
    /// Uses non-blocking <see cref="ChannelWriter{T}.TryWrite"/> because each client channel
    /// is bounded with <see cref="BoundedChannelFullMode.DropOldest"/>, guaranteeing the write
    /// always succeeds without blocking — preventing a slow or disconnected client from stalling
    /// the file watcher pipeline or blocking application shutdown.
    /// </summary>
    /// <param name="notification">The notification to broadcast.</param>
    public void BroadcastNotification(SseNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (string.Equals(notification.Type, "version-update", StringComparison.Ordinal))
        {
            pendingVersionUpdate = notification;
        }

        foreach (var client in clients)
        {
            client.Value.Writer.TryWrite(notification);
        }
    }
}