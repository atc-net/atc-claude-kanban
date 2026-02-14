namespace Atc.Claude.Kanban.EndpointDefinitions;

/// <summary>
/// Defines the Server-Sent Events endpoint at /api/events and the /api/version endpoint.
/// Streams real-time file change notifications to connected browsers.
/// </summary>
public sealed class SseEndpointDefinition : IEndpointDefinition
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc/>
    public void DefineEndpoints(WebApplication app)
    {
        app
            .MapGet("/api/events", StreamEvents)
            .WithName("StreamEvents")
            .WithDescription("Server-Sent Events stream for real-time file change notifications.")
            .WithSummary("Subscribe to real-time events.")
            .WithTags("Events")
            .ExcludeFromDescription();

        app
            .MapGet("/api/version", GetVersion)
            .WithName("GetVersion")
            .WithDescription("Retrieve the current application version.")
            .WithSummary("Retrieve application version.")
            .WithTags("System");

        app
            .MapPost("/api/cache/clear", ClearSnapshotCaches)
            .WithName("ClearSnapshotCaches")
            .WithDescription("Clears in-memory session and task snapshots. Called on page load so completed sessions don't persist across refreshes.")
            .WithSummary("Clear snapshot caches.")
            .WithTags("System");
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "CancellationTokenSource is disposed in the stream callback's finally block")]
    internal static IResult StreamEvents(
        HttpContext httpContext,
        [FromServices] SseClientManager sseClientManager,
        [FromServices] IHostApplicationLifetime lifetime,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var (clientId, channel) = sseClientManager.AddClient();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.ApplicationStopping);

        return Results.Stream(
            async stream =>
            {
                try
                {
                    await WriteSseLineAsync(stream, $"data: {{\"type\":\"connected\",\"clientId\":\"{clientId}\"}}");

                    await StreamEventsAsync(
                        stream,
                        channel.Reader,
                        linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Client disconnected or application stopping — expected
                }
                catch (IOException)
                {
                    // Client disconnected — expected
                }
                finally
                {
                    sseClientManager.RemoveClient(clientId);
                    linkedCts.Dispose();
                }
            },
            contentType: "text/event-stream");
    }

    internal static Ok<VersionResult> GetVersion()
    {
        var version = typeof(SseEndpointDefinition).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return TypedResults.Ok(new VersionResult(version));
    }

    internal static Ok ClearSnapshotCaches(
        [FromServices] TaskService taskService,
        [FromServices] SessionService sessionService)
    {
        taskService.ClearSnapshots();
        sessionService.ClearSnapshots();
        return TypedResults.Ok();
    }

    /// <summary>
    /// Streams SSE events to a single connected client until the connection is closed.
    /// Sends file change notifications with periodic heartbeats to keep the connection alive.
    /// Uses unnamed SSE events (data-only, no "event:" line) so the browser's
    /// <c>EventSource.onmessage</c> handler receives them.
    /// </summary>
    private static async Task StreamEventsAsync(
        Stream stream,
        ChannelReader<SseNotification> reader,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var readTask = reader.WaitToReadAsync(cancellationToken).AsTask();
            var delayTask = Task.Delay(HeartbeatInterval, cancellationToken);

            var completed = await Task.WhenAny(readTask, delayTask);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (completed == delayTask)
            {
                // Heartbeat — keep connection alive
                await WriteSseLineAsync(stream, ": heartbeat");
                continue;
            }

            // Drain all available notifications
            while (reader.TryRead(out var notification))
            {
                var json = JsonSerializer.Serialize(notification, SseJsonOptions);
                await WriteSseLineAsync(stream, $"data: {json}");
            }
        }
    }

    /// <summary>
    /// Writes an SSE line followed by a blank line (event terminator) directly as UTF-8 bytes.
    /// Avoids <see cref="StreamWriter"/> which triggers synchronous <c>Flush()</c> calls
    /// that Kestrel disallows.
    /// </summary>
    private static async Task WriteSseLineAsync(
        Stream stream,
        string line)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{line}\n\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}