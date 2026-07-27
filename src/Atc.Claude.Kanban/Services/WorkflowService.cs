namespace Atc.Claude.Kanban.Services;

/// <summary>
/// Discovers Workflow-tool scripts and reconstructs workflow run state.
/// Scripts live at projects/{hash}/{sessionId}/workflows/scripts/{name}-{runId}.js and their run
/// artifacts at projects/{hash}/{sessionId}/subagents/workflows/{runId}/. A script's project
/// directory can differ from the session's own, so the index scans every project directory and is
/// cached for 5 seconds; the session scan then costs a single lookup per card.
/// </summary>
public sealed class WorkflowService
{
    private const string IndexCacheKey = "workflow-index";

    private static readonly TimeSpan IndexTtl = TimeSpan.FromSeconds(5);

    // The Workflow tool names a script "{name}-{runId}.js"; the run id is the trailing wf_ token.
    private static readonly Regex ScriptNamePattern = new(
        @"^(?<name>.*)-(?<id>wf_[a-z0-9-]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly string claudeDir;
    private readonly IMemoryCache cache;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly SubagentService subagentService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowService"/> class.
    /// </summary>
    /// <param name="claudeDir">Path to the ~/.claude directory.</param>
    /// <param name="cache">Memory cache for the workflow script index.</param>
    /// <param name="jsonSerializerOptions">The shared JSON serializer options.</param>
    /// <param name="subagentService">Service supplying the run's agent transcripts.</param>
    public WorkflowService(
        string claudeDir,
        IMemoryCache cache,
        JsonSerializerOptions jsonSerializerOptions,
        SubagentService subagentService)
    {
        this.claudeDir = claudeDir;
        this.cache = cache;
        this.jsonSerializerOptions = jsonSerializerOptions;
        this.subagentService = subagentService;
    }

    /// <summary>
    /// Returns whether a session has workflow scripts and how many, for the session card badge.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>A tuple of (has workflows, workflow count).</returns>
    public (bool HasWorkflow, int WorkflowCount) GetWorkflowSummary(
        string sessionId)
    {
        var scripts = FindScripts(sessionId);
        return (scripts.Count > 0, scripts.Count);
    }

    /// <summary>
    /// Returns a session's workflow scripts, newest first, including the name and description
    /// declared in each script's meta block.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The session's workflow scripts.</returns>
    public async Task<IReadOnlyList<WorkflowScriptInfo>> GetWorkflowsForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var scripts = FindScripts(sessionId);
        var result = new List<WorkflowScriptInfo>(scripts.Count);

        foreach (var script in scripts)
        {
            var meta = await ReadMetaAsync(script.Path, cancellationToken);
            result.Add(new WorkflowScriptInfo
            {
                Id = script.Id,
                Name = meta.Name ?? script.Name,
                Description = meta.Description,
                ModifiedAt = script.ModifiedAt,
            });
        }

        return result;
    }

    /// <summary>
    /// Returns a workflow script's source, or null when the session has no such workflow.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="workflowId">The workflow run identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The script source, or null.</returns>
    public async Task<WorkflowSourceResponse?> GetWorkflowSourceAsync(
        string sessionId,
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        var script = FindScript(sessionId, workflowId);
        if (script is null)
        {
            return null;
        }

        try
        {
            var content = await File.ReadAllTextAsync(script.Path, cancellationToken);
            var meta = ParseMeta(content);
            return new WorkflowSourceResponse(script.Id, meta.Name ?? script.Name, content);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the absolute path of a workflow script so it can be opened in an editor, or null
    /// when the session has no such workflow. The path comes from the trusted index rather than
    /// being built from the supplied identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="workflowId">The workflow run identifier.</param>
    /// <returns>The script path, or null.</returns>
    public string? GetWorkflowScriptPath(
        string sessionId,
        string workflowId)
        => FindScript(sessionId, workflowId)?.Path;

    /// <summary>
    /// Reconstructs a workflow run: the phases declared in its script plus the agent roster built
    /// from the run directory. Returns null when the session has no such workflow.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="workflowId">The workflow run identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The run state, or null.</returns>
    public async Task<WorkflowRunResponse?> GetWorkflowRunAsync(
        string sessionId,
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        var script = FindScript(sessionId, workflowId);
        if (script is null)
        {
            return null;
        }

        var meta = await ReadMetaAsync(script.Path, cancellationToken);
        var journal = await ReadJournalAsync(script.OwnerSessionId, workflowId, cancellationToken);

        // Take metadata from the agents' own transcripts, but the run status from the journal:
        // transcript timestamps only say when an agent last wrote, not whether it finished.
        var subagents = await subagentService.GetSubagentsForSessionAsync(script.OwnerSessionId, cancellationToken);

        var agents = new List<WorkflowAgentInfo>();
        foreach (var agent in subagents)
        {
            if (!string.Equals(agent.WorkflowRunId, workflowId, StringComparison.Ordinal))
            {
                continue;
            }

            agents.Add(new WorkflowAgentInfo
            {
                AgentId = agent.AgentId,
                Model = agent.Model,
                Description = agent.AgentDescription ?? agent.Description,
                Status = journal.TryGetValue(agent.AgentId, out var entry) && entry.Done ? "done" : "running",
                StartedAt = agent.StartedAt,
                DurationMs = agent.DurationMs,
                ToolUses = agent.ToolUses,
                Result = agent.LastMessage,
            });
        }

        agents.Sort((left, right) => Nullable.Compare(left.StartedAt, right.StartedAt));

        var startedCount = journal.Values.Count(entry => entry.Started);
        var doneCount = journal.Values.Count(entry => entry.Done);
        if (startedCount == 0)
        {
            // No journal on disk — fall back to what the transcripts themselves show.
            startedCount = agents.Count;
            doneCount = agents.Count(agent => string.Equals(agent.Status, "done", StringComparison.Ordinal));
        }

        return new WorkflowRunResponse(
            script.Id,
            meta.Name ?? script.Name,
            meta.Description,
            meta.Phases,
            agents,
            startedCount,
            doneCount,
            agents.Count > 0 ? agents[0].StartedAt : null,
            ResolveStoppedAt(subagents, workflowId));
    }

    private static DateTime? ResolveStoppedAt(
        IReadOnlyList<SubagentInfo> subagents,
        string workflowId)
    {
        DateTime? stoppedAt = null;
        foreach (var agent in subagents)
        {
            if (string.Equals(agent.WorkflowRunId, workflowId, StringComparison.Ordinal) &&
                (stoppedAt is null || agent.LastActivityAt > stoppedAt))
            {
                stoppedAt = agent.LastActivityAt;
            }
        }

        return stoppedAt;
    }

    /// <summary>
    /// Reads the run journal, which records a "started" and then a "result" event per agent.
    /// The result event is the only durable evidence that an agent completed.
    /// </summary>
    private async Task<Dictionary<string, (bool Started, bool Done)>> ReadJournalAsync(
        string sessionId,
        string workflowId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, (bool Started, bool Done)>(StringComparer.Ordinal);
        var journalPath = ResolveJournalPath(sessionId, workflowId);
        if (journalPath is null)
        {
            return result;
        }

        try
        {
            using var reader = new StreamReader(journalPath);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.Length == 0 || line[0] != '{')
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("agentId", out var agentIdEl) ||
                        agentIdEl.GetString() is not { Length: > 0 } agentId)
                    {
                        continue;
                    }

                    var eventType = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                    result.TryGetValue(agentId, out var entry);
                    result[agentId] = eventType switch
                    {
                        "started" => (true, entry.Done),
                        "result" => (entry.Started, true),
                        _ => entry,
                    };
                }
                catch (JsonException)
                {
                    // Skip malformed journal lines
                }
            }
        }
        catch (IOException)
        {
            // Journal may be missing or locked — treat as no journal
        }

        return result;
    }

    private string? ResolveJournalPath(
        string sessionId,
        string workflowId)
    {
        var projectsDir = Path.Combine(claudeDir, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return null;
        }

        foreach (var hashDir in Directory.GetDirectories(projectsDir))
        {
            var runDir = Path.Combine(hashDir, sessionId, "subagents", "workflows", workflowId);
            var journalPath = Path.Combine(runDir, "journal.jsonl");
            if (File.Exists(journalPath))
            {
                return journalPath;
            }
        }

        return null;
    }

    private static async Task<WorkflowMeta> ReadMetaAsync(
        string scriptPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            return ParseMeta(content);
        }
        catch (IOException)
        {
            return new WorkflowMeta(null, null, []);
        }
    }

    /// <summary>
    /// Extracts name, description and phases from a script's meta block by pattern-matching the
    /// source. The Workflow tool requires meta to be a literal, so the values can be read without
    /// executing the script — which must never happen, as these scripts are agent-authored.
    /// </summary>
    /// <param name="source">The workflow script source.</param>
    /// <returns>The values declared in the script's meta block.</returns>
    internal static WorkflowMeta ParseMeta(string source)
    {
        var name = MatchQuotedValue(source, "name");
        var description = MatchQuotedValue(source, "description");

        var phases = new List<WorkflowPhase>();
        var phasesMatch = Regex.Match(
            source,
            @"phases\s*:\s*\[(?<body>[\s\S]*?)\]",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        if (phasesMatch.Success)
        {
            foreach (var entry in Regex.Matches(
                         phasesMatch.Groups["body"].Value,
                         @"\{[\s\S]*?\}",
                         RegexOptions.CultureInvariant,
                         TimeSpan.FromSeconds(1)))
            {
                var entryText = ((Match)entry).Value;
                var title = MatchQuotedValue(entryText, "title");
                if (title is not null)
                {
                    phases.Add(new WorkflowPhase { Title = title, Detail = MatchQuotedValue(entryText, "detail") });
                }
            }
        }

        return new WorkflowMeta(name, description, phases);
    }

    /// <summary>
    /// Reads a quoted property value, accepting single, double or backtick quotes — scripts are
    /// hand-written JavaScript and use all three.
    /// </summary>
    private static string? MatchQuotedValue(
        string source,
        string key)
    {
        var match = Regex.Match(
            source,
            key + @"\s*:\s*(?<quote>['""`])(?<value>[\s\S]*?)\k<quote>",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        return match.Success ? match.Groups["value"].Value : null;
    }

    private WorkflowScriptEntry? FindScript(
        string sessionId,
        string workflowId)
        => FindScripts(sessionId).FirstOrDefault(
            script => string.Equals(script.Id, workflowId, StringComparison.Ordinal));

    /// <summary>
    /// Returns a session's scripts newest-first. A team session's scripts live under the lead
    /// session's directory, so fall back to the team's lead when the raw id has none.
    /// </summary>
    private List<WorkflowScriptEntry> FindScripts(string sessionId)
    {
        var index = GetIndex();
        if (index.TryGetValue(sessionId, out var scripts) && scripts.Count > 0)
        {
            return scripts;
        }

        var leadSessionId = ResolveLeadSessionId(sessionId);
        if (leadSessionId is not null && index.TryGetValue(leadSessionId, out var leadScripts))
        {
            return leadScripts;
        }

        return [];
    }

    private string? ResolveLeadSessionId(string sessionId)
    {
        var configFile = Path.Combine(claudeDir, "teams", sessionId, "config.json");
        if (!File.Exists(configFile))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(configFile);
            var config = JsonSerializer.Deserialize<TeamConfig>(json, jsonSerializerOptions);
            return string.Equals(config?.LeadSessionId, sessionId, StringComparison.Ordinal)
                ? null
                : config?.LeadSessionId;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private Dictionary<string, List<WorkflowScriptEntry>> GetIndex()
    {
        if (cache.TryGetValue(IndexCacheKey, out Dictionary<string, List<WorkflowScriptEntry>>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var index = BuildIndex();
        cache.Set(IndexCacheKey, index, IndexTtl);
        return index;
    }

    /// <summary>
    /// Scans every project directory for workflow scripts, reading filenames only so the session
    /// scan never pays for script contents.
    /// </summary>
    private Dictionary<string, List<WorkflowScriptEntry>> BuildIndex()
    {
        var index = new Dictionary<string, List<WorkflowScriptEntry>>(StringComparer.Ordinal);
        var projectsDir = Path.Combine(claudeDir, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return index;
        }

        foreach (var hashDir in Directory.GetDirectories(projectsDir))
        {
            foreach (var sessionDir in Directory.GetDirectories(hashDir))
            {
                var scriptsDir = Path.Combine(sessionDir, "workflows", "scripts");
                if (!Directory.Exists(scriptsDir))
                {
                    continue;
                }

                var sessionId = Path.GetFileName(sessionDir);
                foreach (var file in Directory.GetFiles(scriptsDir, "*.js"))
                {
                    var entry = BuildEntry(file, sessionId);
                    if (entry is null)
                    {
                        continue;
                    }

                    if (!index.TryGetValue(sessionId, out var list))
                    {
                        list = [];
                        index[sessionId] = list;
                    }

                    list.Add(entry);
                }
            }
        }

        foreach (var list in index.Values)
        {
            list.Sort((left, right) => right.ModifiedAt.CompareTo(left.ModifiedAt));
        }

        return index;
    }

    private static WorkflowScriptEntry? BuildEntry(
        string file,
        string sessionId)
    {
        DateTime modifiedAt;
        try
        {
            modifiedAt = File.GetLastWriteTimeUtc(file);
        }
        catch (IOException)
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(file);
        var match = ScriptNamePattern.Match(baseName);
        return new WorkflowScriptEntry(
            match.Success ? match.Groups["id"].Value : baseName,
            match.Success ? match.Groups["name"].Value : baseName,
            file,
            sessionId,
            modifiedAt);
    }

    /// <summary>
    /// A workflow script located by the index, together with the session that owns its directory.
    /// </summary>
    private sealed record WorkflowScriptEntry(
        string Id,
        string Name,
        string Path,
        string OwnerSessionId,
        DateTime ModifiedAt);

    /// <summary>
    /// The values declared in a workflow script's meta block.
    /// </summary>
    internal sealed record WorkflowMeta(
        string? Name,
        string? Description,
        IReadOnlyList<WorkflowPhase> Phases);
}