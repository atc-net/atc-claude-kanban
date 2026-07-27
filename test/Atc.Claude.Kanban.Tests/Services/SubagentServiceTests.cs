namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="SubagentService"/>.
/// </summary>
public sealed class SubagentServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache cache;

    public SubagentServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-test-" + Guid.NewGuid().ToString("N"));
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
    public async Task GetSubagents_ReturnsEmpty_WhenNoProjectsDirectory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("some-session", cancellationToken);

        // Assert
        subagents.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSubagents_ReturnsEmpty_WhenNoSubagentsDirectory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash123", "session-1");
        Directory.CreateDirectory(projectDir);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-1", cancellationToken);

        // Assert
        subagents.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSubagents_ParsesSubagentFile()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash123", "session-x", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", slug = "explore-code", timestamp = "2025-06-01T10:00:00Z", cwd = "/home/user/project", message = new { content = "Find all API endpoints in the codebase" } }),
            JsonSerializer.Serialize(new { type = "assistant", message = new { model = "claude-opus-4-6", content = "I'll search for API endpoints." } }));

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-abc1234.jsonl"),
            jsonl,
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-x", cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        var agent = subagents[0];
        agent.AgentId.Should().Be("abc1234");
        agent.SessionId.Should().Be("session-x");
        agent.Slug.Should().Be("explore-code");
        agent.Model.Should().Be("claude-opus-4-6");
        agent.Cwd.Should().Be("/home/user/project");
        agent.Description.Should().Contain("Find all API endpoints");
    }

    [Fact]
    public async Task GetSubagents_CleansTeammateMessageDescription()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash789", "session-tm", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = "2025-06-01T10:00:00Z",
            message = new
            {
                content = "<teammate-message teammate_id=\"team-lead\" summary=\"New features evaluator\">\nYou are the \"features-evaluator\" agent.",
            },
        });

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-tm123.jsonl"),
            jsonl,
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-tm", cancellationToken);

        // Assert — should extract the summary, not the raw XML tag
        subagents.Should().HaveCount(1);
        subagents[0].Description.Should().Be("New features evaluator");
    }

    [Fact]
    public void GetSubagentCounts_ReturnsZero_WhenNoProjects()
    {
        // Arrange
        var service = new SubagentService(tempDir, cache);

        // Act
        var (total, active) = service.GetSubagentCounts("some-session");

        // Assert
        total.Should().Be(0);
        active.Should().Be(0);
    }

    [Fact]
    public async Task GetSubagentCounts_CountsFiles()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash456", "session-y", "subagents");
        Directory.CreateDirectory(subagentsDir);

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-aaa.jsonl"),
            "{\"type\":\"user\"}",
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-bbb.jsonl"),
            "{\"type\":\"user\"}",
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var (total, active) = service.GetSubagentCounts("session-y");

        // Assert
        total.Should().Be(2);

        // Both files were just created, so they are within the 15s active threshold
        active.Should().Be(2);
    }

    [Fact]
    public async Task GetSubagentCounts_ActiveCountMatchesActiveStatuses_InDetailedListing()
    {
        // Arrange — the lightweight count and the detailed listing must agree on "active".
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash-agree", "session-agree", "subagents");
        Directory.CreateDirectory(subagentsDir);

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-fresh.jsonl"),
            "{\"type\":\"user\"}",
            cancellationToken);

        var idleFile = Path.Combine(subagentsDir, "agent-stale.jsonl");
        await File.WriteAllTextAsync(idleFile, "{\"type\":\"user\"}", cancellationToken);
        File.SetLastWriteTimeUtc(idleFile, DateTime.UtcNow.AddSeconds(-45));

        var service = new SubagentService(tempDir, cache);

        // Act
        var (_, active) = service.GetSubagentCounts("session-agree");
        var subagents = await service.GetSubagentsForSessionAsync("session-agree", cancellationToken);

        // Assert
        var activeStatuses = subagents.Count(s => s.Status == "active");
        active.Should().Be(activeStatuses);
    }

    [Fact]
    public async Task GetSubagents_ReturnsActiveStatus_WhenRecentlyModified()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash-active", "session-active", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = JsonSerializer.Serialize(new { type = "user", timestamp = "2025-06-01T10:00:00Z", message = new { content = "Test task" } });
        var filePath = Path.Combine(subagentsDir, "agent-act1.jsonl");
        await File.WriteAllTextAsync(filePath, jsonl, cancellationToken);

        // File was just created, so it's within the 15s active threshold
        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-active", cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        subagents[0].Status.Should().Be("active");
    }

    [Fact]
    public async Task GetSubagents_ReturnsIdleStatus_WhenModeratelyOld()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash-idle", "session-idle", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = JsonSerializer.Serialize(new { type = "user", timestamp = "2025-06-01T10:00:00Z", message = new { content = "Test task" } });
        var filePath = Path.Combine(subagentsDir, "agent-idl1.jsonl");
        await File.WriteAllTextAsync(filePath, jsonl, cancellationToken);

        // Set modification time to 30 seconds ago (between 15s active and 90s idle thresholds)
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddSeconds(-30));

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-idle", cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        subagents[0].Status.Should().Be("idle");
    }

    [Fact]
    public async Task GetSubagents_ReturnsStoppedStatus_WhenOld()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash-stop", "session-stop", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = JsonSerializer.Serialize(new { type = "user", timestamp = "2025-06-01T10:00:00Z", message = new { content = "Test task" } });
        var filePath = Path.Combine(subagentsDir, "agent-stp1.jsonl");
        await File.WriteAllTextAsync(filePath, jsonl, cancellationToken);

        // Set modification time to 2 minutes ago (beyond 90s idle threshold)
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-2));

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-stop", cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        subagents[0].Status.Should().Be("stopped");
    }

    [Fact]
    public async Task GetSubagents_IncludesLastMessage_WhenStopped()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash-msg", "session-msg", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", timestamp = "2025-06-01T10:00:00Z", message = new { content = "Find bugs" } }),
            JsonSerializer.Serialize(new { type = "assistant", message = new { model = "claude-opus-4-6", content = "I found 3 bugs in the codebase." } }));

        var filePath = Path.Combine(subagentsDir, "agent-msg1.jsonl");
        await File.WriteAllTextAsync(filePath, jsonl, cancellationToken);

        // Set modification time to 2 minutes ago so agent is stopped
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-2));

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-msg", cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        subagents[0].LastMessage.Should().Be("I found 3 bugs in the codebase.");
    }

    [Fact]
    public async Task GetSubagents_DoesNotIncludeLastMessage_WhenActive()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash-nomsg", "session-nomsg", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", timestamp = "2025-06-01T10:00:00Z", message = new { content = "Find bugs" } }),
            JsonSerializer.Serialize(new { type = "assistant", message = new { model = "claude-opus-4-6", content = "I found 3 bugs." } }));

        var filePath = Path.Combine(subagentsDir, "agent-nomsg.jsonl");
        await File.WriteAllTextAsync(filePath, jsonl, cancellationToken);

        // File just created = active, so lastMessage should NOT be populated
        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-nomsg", cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        subagents[0].Status.Should().Be("active");
        subagents[0].LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task GetSubagentCounts_CountsOnlyRunningAgentsAsActive()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash-cnt", "session-cnt", "subagents");
        Directory.CreateDirectory(subagentsDir);

        // Create 3 agent files
        var file1 = Path.Combine(subagentsDir, "agent-cnt1.jsonl");
        var file2 = Path.Combine(subagentsDir, "agent-cnt2.jsonl");
        var file3 = Path.Combine(subagentsDir, "agent-cnt3.jsonl");

        await File.WriteAllTextAsync(file1, "{\"type\":\"user\"}", cancellationToken);
        await File.WriteAllTextAsync(file2, "{\"type\":\"user\"}", cancellationToken);
        await File.WriteAllTextAsync(file3, "{\"type\":\"user\"}", cancellationToken);

        // file1: just created = active, file2: 30s ago = idle, file3: 2min ago = stopped
        File.SetLastWriteTimeUtc(file2, DateTime.UtcNow.AddSeconds(-30));
        File.SetLastWriteTimeUtc(file3, DateTime.UtcNow.AddMinutes(-2));

        var service = new SubagentService(tempDir, cache);

        // Act
        var (total, active) = service.GetSubagentCounts("session-cnt");

        // Assert
        total.Should().Be(3);

        // Only the still-running agent is active; idle and stopped agents are finished work
        // and must not keep the session in the active filter.
        active.Should().Be(1);
    }

    [Fact]
    public async Task GetSubagents_MarksRejectedAgentAsStopped()
    {
        // Arrange — fresh subagent file (would be "active" by mtime) for an agent
        // whose Agent tool_use was rejected by the user. Status must be overridden to "stopped".
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-rej";
        const string agentId = "rej123";
        const string toolUseId = "toolu_rejected";

        var hashDir = Path.Combine(tempDir, "projects", "hash-rej");
        var subagentsDir = Path.Combine(hashDir, sessionId, "subagents");
        Directory.CreateDirectory(subagentsDir);

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, $"agent-{agentId}.jsonl"),
            JsonSerializer.Serialize(new { type = "user", timestamp = "2026-05-10T10:00:00Z", message = new { content = "spawn me" } }),
            cancellationToken);

        var sessionJsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new
                {
                    role = "assistant",
                    content = new object[]
                    {
                        new { type = "tool_use", id = toolUseId, name = "Agent", input = new { description = "Run something" } },
                    },
                },
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                message = new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "tool_result", tool_use_id = toolUseId, content = "User rejected tool use" },
                    },
                },
            }),
            JsonSerializer.Serialize(new { agent_progress = true, agentId, parentToolUseID = toolUseId }));

        await File.WriteAllTextAsync(
            Path.Combine(hashDir, $"{sessionId}.jsonl"),
            sessionJsonl,
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync(sessionId, cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        subagents[0].AgentId.Should().Be(agentId);
        subagents[0].Status.Should().Be("stopped");
    }

    [Fact]
    public async Task GetSubagents_PopulatesToolUsesAndDuration_FromToolUseResult()
    {
        // Arrange — a parent toolUseResult completion line reports the agent's tool
        // count and active duration; these must surface on the SubagentInfo.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-usage";
        const string agentId = "usage789";
        const string toolUseId = "toolu_usage";

        var hashDir = Path.Combine(tempDir, "projects", "hash-usage");
        var subagentsDir = Path.Combine(hashDir, sessionId, "subagents");
        Directory.CreateDirectory(subagentsDir);

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, $"agent-{agentId}.jsonl"),
            JsonSerializer.Serialize(new { type = "user", timestamp = "2026-05-10T10:00:00Z", message = new { content = "spawn me" } }),
            cancellationToken);

        var sessionJsonl = JsonSerializer.Serialize(new
        {
            type = "user",
            toolUseResult = new { agentId, totalTokens = 93913, totalToolUseCount = 42, totalDurationMs = 150505 },
            message = new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "tool_result", tool_use_id = toolUseId, content = "done" },
                },
            },
        });

        await File.WriteAllTextAsync(
            Path.Combine(hashDir, $"{sessionId}.jsonl"),
            sessionJsonl,
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync(sessionId, cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        subagents[0].AgentId.Should().Be(agentId);
        subagents[0].ToolUses.Should().Be(42);
        subagents[0].DurationMs.Should().Be(150505);
    }

    [Fact]
    public async Task GetSubagents_MarksKilledAgentAsStopped()
    {
        // Arrange — fresh subagent file but the parent JSONL has a task-notification
        // marking this agent as killed; status must be overridden to "stopped".
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-kill";
        const string agentId = "kill456";

        var hashDir = Path.Combine(tempDir, "projects", "hash-kill");
        var subagentsDir = Path.Combine(hashDir, sessionId, "subagents");
        Directory.CreateDirectory(subagentsDir);

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, $"agent-{agentId}.jsonl"),
            JsonSerializer.Serialize(new { type = "user", timestamp = "2026-05-10T10:00:00Z", message = new { content = "doing work" } }),
            cancellationToken);

        // Real Claude Code JSONL keeps angle brackets unescaped, so write the line by hand.
        var sessionJsonl =
            $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":" +
            $"\"<task-notification><task-id>{agentId}</task-id><status>killed</status></task-notification>\"}}}}";

        await File.WriteAllTextAsync(
            Path.Combine(hashDir, $"{sessionId}.jsonl"),
            sessionJsonl,
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync(sessionId, cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        subagents[0].AgentId.Should().Be(agentId);
        subagents[0].Status.Should().Be("stopped");
    }

    [Fact]
    public async Task GetSubagents_DiscoversWorkflowSubagents_NestedUnderRunDirectory()
    {
        // Arrange — workflow-spawned agents live under subagents/workflows/{runId}/, and the
        // run's journal.jsonl sits beside them but must not be treated as an agent.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-wf";
        var subagentsDir = Path.Combine(tempDir, "projects", "hash-wf", sessionId, "subagents");
        var runDir = Path.Combine(subagentsDir, "workflows", "wf_abc123");
        Directory.CreateDirectory(runDir);

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-flat001.jsonl"),
            BuildAssistantTranscript("Flat agent reply"),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(runDir, "agent-nested001.jsonl"),
            BuildAssistantTranscript("Nested agent reply"),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(runDir, "journal.jsonl"),
            JsonSerializer.Serialize(new { type = "started", agentId = "nested001" }),
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync(sessionId, cancellationToken);
        var counts = service.GetSubagentCounts(sessionId);

        // Assert — both agents found, journal excluded.
        subagents.Select(s => s.AgentId).Should().BeEquivalentTo("flat001", "nested001");
        counts.Total.Should().Be(2);

        // The run id marks the workflow agent so the UI can distinguish it; a regular
        // subagent sitting directly in subagents/ carries none.
        subagents.Single(s => s.AgentId == "nested001").WorkflowRunId.Should().Be("wf_abc123");
        subagents.Single(s => s.AgentId == "flat001").WorkflowRunId.Should().BeNull();
    }

    [Fact]
    public async Task GetSubagents_UsesStructuredOutput_WhenResultExceedsLastMessageTail()
    {
        // Arrange — a schema'd workflow agent emits no text response, only a forced
        // StructuredOutput call. That call is not the final line and is larger than the
        // last-message tail window, so it is only found by the wider structured-result read.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-so";
        var runDir = Path.Combine(tempDir, "projects", "hash-so", sessionId, "subagents", "workflows", "wf_so");
        Directory.CreateDirectory(runDir);

        // Padding pushes the StructuredOutput line well beyond the 5120-byte tail read.
        var padding = new string('x', 6000);
        var lines = new[]
        {
            JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = "Verify the finding" } }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new
                {
                    role = "assistant",
                    model = "claude-opus-4-8",
                    content = new object[]
                    {
                        new
                        {
                            type = "tool_use",
                            id = "tu-so",
                            name = "StructuredOutput",
                            input = new { finding = "Counts use mtime", confirmed = true, evidence = padding },
                        },
                    },
                },
            }),
            JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = "tu-so", content = "ok" } } } }),
        };

        var filePath = Path.Combine(runDir, "agent-so001.jsonl");
        await File.WriteAllTextAsync(filePath, string.Join("\n", lines), cancellationToken);

        // A result is only read once the agent is no longer active (avoids reading mid-write).
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-2));

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync(sessionId, cancellationToken);

        // Assert — the structured result stands in for the missing text reply.
        var agent = subagents.Should().ContainSingle().Subject;
        agent.AgentId.Should().Be("so001");
        agent.LastMessage.Should().NotBeNullOrEmpty();
        agent.LastMessage.Should().Contain("finding: Counts use mtime");
        agent.LastMessage.Should().Contain("confirmed: true");
    }

    private static string BuildAssistantTranscript(string text)
        => string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = "Do the thing" } }),
            JsonSerializer.Serialize(new { type = "assistant", message = new { role = "assistant", model = "claude-opus-4-8", content = new object[] { new { type = "text", text } } } }));
}