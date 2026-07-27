namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="UsageService"/>.
/// </summary>
public sealed class UsageServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache cache;

    public UsageServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-usage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        cache = new MemoryCache(new MemoryCacheOptions());
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
    public async Task GetUsage_IncludesLeadSessionAndSubagentRows()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hashU");
        Directory.CreateDirectory(projectDir);

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-u.jsonl"),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new
                {
                    model = "claude-opus-4-6",
                    content = new[] { new { type = "text", text = "lead" } },
                    usage = new { input_tokens = 1000, output_tokens = 500, cache_creation_input_tokens = 100, cache_read_input_tokens = 2000 },
                },
            }),
            cancellationToken);

        var subagentsDir = Path.Combine(projectDir, "session-u", "subagents");
        Directory.CreateDirectory(subagentsDir);
        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-sub1.jsonl"),
            string.Join(
                "\n",
                JsonSerializer.Serialize(new { type = "user", slug = "explore", message = new { content = "go" } }),
                JsonSerializer.Serialize(new
                {
                    type = "assistant",
                    message = new
                    {
                        model = "claude-sonnet-4-6",
                        content = new[] { new { type = "text", text = "sub" } },
                        usage = new { input_tokens = 200, output_tokens = 100, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
                    },
                })),
            cancellationToken);

        var service = new UsageService(
            new SessionActivityService(tempDir, cache),
            new SubagentService(tempDir, cache));

        // Act
        var usage = await service.GetUsageAsync("session-u", cancellationToken);

        // Assert
        usage.Should().NotBeNull();
        usage!.ContextTokens.Should().Be(3100); // latest lead turn: 1000 + 2000 + 100
        usage.Rows.Should().HaveCount(2);
        usage.Rows[0].Kind.Should().Be("session");
        usage.Rows[0].Label.Should().Be("Session");
        usage.Rows[0].Model.Should().Be("claude-opus-4-6");
        usage.Rows[0].Models.Should().ContainSingle(model => model.Model == "claude-opus-4-6");

        var agentRow = usage.Rows[1];
        agentRow.Kind.Should().Be("agent");
        agentRow.Model.Should().Be("claude-sonnet-4-6");
        agentRow.TotalTokens.Should().Be(300); // 200 + 100
        agentRow.WorkflowRunId.Should().BeNull();
        usage.TotalCostUsd.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetUsage_MarksWorkflowSubagentRows_WithTheirRunId()
    {
        // Arrange — a workflow-spawned agent must be attributable in the usage breakdown,
        // not indistinguishable from an Agent-tool subagent.
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hashW");
        Directory.CreateDirectory(projectDir);

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-w.jsonl"),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new
                {
                    model = "claude-opus-4-8",
                    content = new[] { new { type = "text", text = "lead" } },
                    usage = new { input_tokens = 900, output_tokens = 400, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
                },
            }),
            cancellationToken);

        var runDir = Path.Combine(projectDir, "session-w", "subagents", "workflows", "wf_run77");
        Directory.CreateDirectory(runDir);

        // Timestamps 45s apart plus two tool calls: a workflow agent has no Agent-tool completion
        // record, so these stats must be derived from the transcript itself.
        var agentFile = Path.Combine(runDir, "agent-wfa1.jsonl");
        await File.WriteAllTextAsync(
            agentFile,
            string.Join(
                "\n",
                JsonSerializer.Serialize(new { type = "user", slug = "verify-finding", timestamp = "2026-07-23T12:00:00.000Z", message = new { content = "verify" } }),
                JsonSerializer.Serialize(new
                {
                    type = "assistant",
                    timestamp = "2026-07-23T12:00:20.000Z",
                    message = new
                    {
                        model = "claude-sonnet-4-6",
                        content = new object[] { new { type = "tool_use", id = "t1", name = "Read", input = new { file_path = "a.cs" } } },
                        usage = new { input_tokens = 100, output_tokens = 50, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "assistant",
                    timestamp = "2026-07-23T12:00:45.000Z",
                    message = new
                    {
                        model = "claude-sonnet-4-6",
                        content = new object[] { new { type = "tool_use", id = "t2", name = "StructuredOutput", input = new { finding = "ok" } } },
                        usage = new { input_tokens = 200, output_tokens = 100, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
                    },
                })),
            cancellationToken);

        File.SetLastWriteTimeUtc(agentFile, DateTime.UtcNow.AddMinutes(-2));

        var service = new UsageService(
            new SessionActivityService(tempDir, cache),
            new SubagentService(tempDir, cache));

        // Act
        var usage = await service.GetUsageAsync("session-w", cancellationToken);

        // Assert
        usage.Should().NotBeNull();
        var agentRow = usage!.Rows.Should().ContainSingle(r => r.Kind == "agent").Subject;
        agentRow.WorkflowRunId.Should().Be("wf_run77");
        agentRow.Label.Should().Be("verify-finding");

        // Tool count and duration are derived from the transcript, not the missing Agent-tool record.
        agentRow.ToolUses.Should().Be(2);
        agentRow.DurationMs.Should().Be(45_000);
    }
}