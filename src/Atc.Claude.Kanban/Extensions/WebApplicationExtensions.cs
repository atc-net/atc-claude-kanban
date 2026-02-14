namespace Atc.Claude.Kanban.Extensions;

/// <summary>
/// Extension methods for configuring the WebApplication middleware pipeline.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Configures embedded static files and the SPA fallback to index.html.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UseEmbeddedStaticFiles(this WebApplication app)
    {
        var embeddedProvider = new ManifestEmbeddedFileProvider(
            typeof(WebApplicationExtensions).Assembly,
            "wwwroot");

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = embeddedProvider,
        });

        // SPA fallback — serve index.html for non-API routes
        app.MapFallback(async context =>
        {
            var indexFile = embeddedProvider.GetFileInfo("index.html");
            if (!indexFile.Exists)
            {
                context.Response.StatusCode = 404;
                return;
            }

            context.Response.ContentType = "text/html";
            await using var stream = indexFile.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        });

        return app;
    }
}