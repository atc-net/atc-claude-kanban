namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="WorkflowService"/>.
/// </summary>
public sealed class WorkflowServiceTests : IDisposable
{
    private const string SessionId = "11111111-1111-4111-8111-111111111111";
    private const string RunId = "wf_abc123-99";

    private readonly string tempDir;
    private readonly MemoryCache cache;
    private readonly JsonSerializerOptions jsonSerializerOptions;

    public WorkflowServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-wf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        cache = new MemoryCache(new MemoryCacheOptions());
        jsonSerializerOptions = JsonSerializerOptionsFactory.Create();
    }

    public void Dispose()
    {
        cache.Dispose();

        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ParseMeta_ReadsNameDescriptionAndPhases_AcrossQuoteStyles()
    {
        // Arrange — hand-written scripts mix single, double and backtick quotes.
        const string source = """
            export const meta = {
              name: 'audit-pricing',
              description: "Audit pricing rules",
              phases: [
                { title: 'Audit', detail: `one agent per rule` },
                { title: "Verify" },
              ],
            }
            """;

        // Act
        var meta = WorkflowService.ParseMeta(source);

        // Assert
        meta.Name.Should().Be("audit-pricing");
        meta.Description.Should().Be("Audit pricing rules");
        meta.Phases.Select(p => p.Title).Should().BeEquivalentTo("Audit", "Verify");
        meta.Phases[0].Detail.Should().Be("one agent per rule");
        meta.Phases[1].Detail.Should().BeNull();
    }

    [Fact]
    public async Task GetWorkflowsForSession_ReturnsScriptWithMetaNameAndDescription()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        WriteScript("audit-pricing");
        var service = CreateService();

        // Act
        var workflows = await service.GetWorkflowsForSessionAsync(SessionId, cancellationToken);

        // Assert
        var workflow = workflows.Should().ContainSingle().Subject;
        workflow.Id.Should().Be(RunId);
        workflow.Name.Should().Be("audit-pricing");
        workflow.Description.Should().Be("Audit pricing rules");
    }

    [Fact]
    public void GetWorkflowSummary_FlagsSessionsWithScripts()
    {
        // Arrange
        WriteScript("audit-pricing");
        var service = CreateService();

        // Act
        var withScripts = service.GetWorkflowSummary(SessionId);
        var withoutScripts = service.GetWorkflowSummary("22222222-2222-4222-8222-222222222222");

        // Assert
        withScripts.Should().Be((true, 1));
        withoutScripts.Should().Be((false, 0));
    }

    [Fact]
    public async Task GetWorkflowRun_TakesAgentStatusFromTheJournal_NotFromTranscriptAge()
    {
        // Arrange — both transcripts are old enough that their timestamp-derived status is
        // "stopped", but the journal recorded a result for only one of them. The run view must
        // report the journal's view: one done, one still running.
        var cancellationToken = TestContext.Current.CancellationToken;
        WriteScript("audit-pricing");

        var runDir = Path.Combine(tempDir, "projects", "proj", SessionId, "subagents", "workflows", RunId);
        Directory.CreateDirectory(runDir);
        WriteAgentTranscript(runDir, "agentdone");
        WriteAgentTranscript(runDir, "agentbusy");

        await File.WriteAllTextAsync(
            Path.Combine(runDir, "journal.jsonl"),
            string.Join(
                "\n",
                JsonSerializer.Serialize(new { type = "started", agentId = "agentdone" }),
                JsonSerializer.Serialize(new { type = "started", agentId = "agentbusy" }),
                JsonSerializer.Serialize(new { type = "result", agentId = "agentdone" })),
            cancellationToken);

        var service = CreateService();

        // Act
        var run = await service.GetWorkflowRunAsync(SessionId, RunId, cancellationToken);

        // Assert
        run.Should().NotBeNull();
        run!.Name.Should().Be("audit-pricing");
        run.Phases.Should().HaveCount(2);
        run.StartedCount.Should().Be(2);
        run.DoneCount.Should().Be(1);
        run.Agents.Should().HaveCount(2);
        run.Agents.Single(a => a.AgentId == "agentdone").Status.Should().Be("done");
        run.Agents.Single(a => a.AgentId == "agentbusy").Status.Should().Be("running");
    }

    [Fact]
    public async Task GetWorkflowRun_ReturnsNull_ForAnUnknownWorkflowId()
    {
        // Arrange — ids are resolved against the session's own script index, so an unknown or
        // hostile id resolves to nothing rather than being turned into a path.
        var cancellationToken = TestContext.Current.CancellationToken;
        WriteScript("audit-pricing");
        var service = CreateService();

        // Act
        var run = await service.GetWorkflowRunAsync(SessionId, "../../../etc/passwd", cancellationToken);
        var source = await service.GetWorkflowSourceAsync(SessionId, "wf_does-not-exist", cancellationToken);
        var path = service.GetWorkflowScriptPath(SessionId, "wf_does-not-exist");

        // Assert
        run.Should().BeNull();
        source.Should().BeNull();
        path.Should().BeNull();
    }

    private WorkflowService CreateService()
        => new(tempDir, cache, jsonSerializerOptions, new SubagentService(tempDir, cache));

    private void WriteScript(string name)
    {
        var scriptsDir = Path.Combine(tempDir, "projects", "proj", SessionId, "workflows", "scripts");
        Directory.CreateDirectory(scriptsDir);

        const string source = """
            export const meta = {
              name: 'audit-pricing',
              description: 'Audit pricing rules',
              phases: [
                { title: 'Audit', detail: 'one agent per rule' },
                { title: 'Verify' },
              ],
            }
            """;

        File.WriteAllText(Path.Combine(scriptsDir, $"{name}-{RunId}.js"), source);
    }

    private static void WriteAgentTranscript(
        string runDir,
        string agentId)
    {
        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", slug = agentId, timestamp = "2026-07-23T12:00:00.000Z", message = new { content = "do the thing" } }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = "2026-07-23T12:00:20.000Z",
                message = new
                {
                    model = "claude-sonnet-4-6",
                    content = new object[] { new { type = "text", text = "done" } },
                },
            }));

        var filePath = Path.Combine(runDir, $"agent-{agentId}.jsonl");
        File.WriteAllText(filePath, jsonl);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-10));
    }
}