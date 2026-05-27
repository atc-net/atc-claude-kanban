namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Builds a per-participant token/cost breakdown for a session: the lead session
/// plus each of its subagents, alongside the current context size.
/// </summary>
public sealed class UsageService
{
    private readonly SessionActivityService activityService;
    private readonly SubagentService subagentService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageService"/> class.
    /// </summary>
    /// <param name="activityService">Service that accumulates token usage from transcripts.</param>
    /// <param name="subagentService">Service that lists a session's subagents.</param>
    public UsageService(
        SessionActivityService activityService,
        SubagentService subagentService)
    {
        this.activityService = activityService;
        this.subagentService = subagentService;
    }

    /// <summary>
    /// Returns the token/cost breakdown for a session, or null when the session
    /// transcript cannot be found.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The usage breakdown, or null.</returns>
    public async Task<UsageResponse?> GetUsageAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var lead = await activityService.GetTokenUsageAsync(sessionId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var rows = new List<UsageRow> { ToRow("Session", "session", lead, null) };

        var subagents = await subagentService.GetSubagentsForSessionAsync(sessionId, cancellationToken);
        foreach (var agent in subagents)
        {
            if (string.IsNullOrEmpty(agent.TranscriptPath))
            {
                continue;
            }

            var usage = await activityService.GetTokenUsageForPathAsync(agent.TranscriptPath, cancellationToken);
            var label = agent.AgentName ?? agent.Slug ?? agent.AgentId;
            rows.Add(ToRow(label, "agent", usage, agent.Model));
        }

        long totalTokens = 0;
        double totalCost = 0;
        foreach (var row in rows)
        {
            totalTokens += row.TotalTokens;
            totalCost += row.CostUsd;
        }

        return new UsageResponse(sessionId, lead.ContextTokens, rows, totalTokens, totalCost);
    }

    private static UsageRow ToRow(
        string label,
        string kind,
        SessionTokenUsage usage,
        string? fallbackModel)
        => new(label, kind, usage.Model ?? fallbackModel, usage.TotalTokens, usage.CostUsd);
}