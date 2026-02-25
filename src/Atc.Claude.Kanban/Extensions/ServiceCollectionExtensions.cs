namespace Atc.Claude.Kanban.Extensions;

/// <summary>
/// Extension methods for registering application services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Kanban dashboard services in the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKanbanServices(
        this IServiceCollection services,
        string claudeDir)
    {
        services.AddMemoryCache();

        services.AddSingleton(JsonSerializerOptionsFactory.Create());

        services.AddSingleton<SseClientManager>();

        services.AddSingleton(sp => new SubagentService(
            claudeDir,
            sp.GetRequiredService<IMemoryCache>()));

        services.AddSingleton(sp => new SessionService(
            claudeDir,
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<JsonSerializerOptions>(),
            sp.GetRequiredService<SubagentService>()));

        services.AddSingleton(sp => new TaskService(
            claudeDir,
            sp.GetRequiredService<SessionService>(),
            sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddSingleton(sp => new TeamService(
            claudeDir,
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddSingleton(new PlanService(claudeDir));

        services.AddSingleton<IHostedService>(sp => new ClaudeDirectoryWatcher(
                claudeDir,
                sp.GetRequiredService<SseClientManager>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<ILogger<ClaudeDirectoryWatcher>>()));

        return services;
    }

    /// <summary>
    /// Registers the background update check service unless suppressed by environment.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUpdateCheckService(
        this IServiceCollection services)
    {
        if (IsUpdateCheckSuppressed())
        {
            return services;
        }

        services.AddHttpClient();

        services.AddSingleton<IHostedService>(sp => new UpdateCheckService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("UpdateCheck"),
            sp.GetRequiredService<SseClientManager>(),
            sp.GetRequiredService<JsonSerializerOptions>(),
            sp.GetRequiredService<ILogger<UpdateCheckService>>()));

        return services;
    }

    private static bool IsUpdateCheckSuppressed()
        => string.Equals(
               Environment.GetEnvironmentVariable("ATC_NO_UPDATE_CHECK"),
               "1",
               StringComparison.Ordinal)
           || IsRunningInCi();

    private static bool IsRunningInCi()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
}