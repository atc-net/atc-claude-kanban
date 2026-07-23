namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="SessionService"/>.
/// </summary>
public sealed class SessionServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache cache;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly SubagentService subagentService;

    public SessionServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        cache = new MemoryCache(new MemoryCacheOptions());
        jsonSerializerOptions = JsonSerializerOptionsFactory.Create();
        subagentService = new SubagentService(tempDir, cache);
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
    public async Task GetSessions_ReturnsEmpty_WhenNoTasksDirectory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSessions_DiscoversSingleSession()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-abc");

        Directory.CreateDirectory(sessionDir);

        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Test task", status = "pending" }),
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().HaveCount(1);
        sessions[0].Id.Should().Be("session-abc");
        sessions[0].TaskCount.Should().Be(1);
        sessions[0].Pending.Should().Be(1);
    }

    [Fact]
    public async Task GetSessions_ComputesProgressCorrectly()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-progress");

        Directory.CreateDirectory(sessionDir);

        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Task 1", status = "completed" }),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "2.json"),
            JsonSerializer.Serialize(new { id = "2", subject = "Task 2", status = "in_progress" }),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "3.json"),
            JsonSerializer.Serialize(new { id = "3", subject = "Task 3", status = "pending" }),
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().HaveCount(1);
        var session = sessions[0];
        session.TaskCount.Should().Be(3);
        session.Completed.Should().Be(1);
        session.InProgress.Should().Be(1);
        session.Pending.Should().Be(1);
        session.Progress.Should().Be(33);
    }

    [Fact]
    public async Task GetSessions_LimitsResults()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var i = 0; i < 5; i++)
        {
            var sessionDir = Path.Combine(tempDir, "tasks", $"session-{i}");

            Directory.CreateDirectory(sessionDir);

            await File.WriteAllTextAsync(
                Path.Combine(sessionDir, "1.json"),
                JsonSerializer.Serialize(new { id = "1", subject = $"Task {i}", status = "pending" }),
                cancellationToken);
        }

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(limit: 3, cancellationToken: cancellationToken);

        // Assert
        sessions.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetProjects_ReturnsEmpty_WhenNoSessions()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var projects = await service.GetProjectsAsync(cancellationToken);

        // Assert
        projects.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSessions_SkipsMalformedJsonFiles()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-bad");

        Directory.CreateDirectory(sessionDir);

        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "bad.json"),
            "{ this is not valid json }",
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "good.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Good task", status = "pending" }),
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().HaveCount(1);
        sessions[0].TaskCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSessions_IncludesSessionWithNoTaskFiles()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-empty");

        Directory.CreateDirectory(sessionDir);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert — session directory exists so session should appear with taskCount=0
        sessions.Should().HaveCount(1);
        sessions[0].Id.Should().Be("session-empty");
        sessions[0].TaskCount.Should().Be(0);
    }

    [Fact]
    public async Task GetSessions_StrongMetadataWinsOverShadowJsonl()
    {
        // Arrange — same session in two project dirs:
        //   "a-shadow": iterated first; only carries a custom-title record (no cwd) and points at a worktree
        //   "b-strong": iterated second; carries the real cwd / git branch / slug for the main repo
        // The resolved session must reflect the strong project, not the worktree shadow.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-shared";

        var shadowDir = Path.Combine(tempDir, "projects", "a-shadow");
        Directory.CreateDirectory(shadowDir);
        await File.WriteAllTextAsync(
            Path.Combine(shadowDir, "sessions-index.json"),
            JsonSerializer.Serialize(new { originalPath = "/worktree/path", entries = Array.Empty<object>() }),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(shadowDir, $"{sessionId}.jsonl"),
            JsonSerializer.Serialize(new { type = "custom-title", customTitle = "From worktree" }),
            cancellationToken);

        var strongDir = Path.Combine(tempDir, "projects", "b-strong");
        Directory.CreateDirectory(strongDir);
        await File.WriteAllTextAsync(
            Path.Combine(strongDir, "sessions-index.json"),
            JsonSerializer.Serialize(new { originalPath = "/main/repo", entries = Array.Empty<object>() }),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(strongDir, $"{sessionId}.jsonl"),
            JsonSerializer.Serialize(new
            {
                type = "user",
                cwd = "/main/repo",
                gitBranch = "feature/foo",
                slug = "real-slug",
                message = new { role = "user", content = "Hello" },
            }),
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert — the strong entry's project/cwd/slug must take precedence even though the shadow was seen first
        sessions.Should().HaveCount(1);
        var session = sessions[0];
        session.Id.Should().Be(sessionId);
        session.Project.Should().Be("/main/repo");
        session.Cwd.Should().Be("/main/repo");
        session.GitBranch.Should().Be("feature/foo");
        session.Slug.Should().Be("real-slug");
        session.Name.Should().Be("From worktree");
    }

    [Fact]
    public async Task GetSessions_SurfacesActiveGoal()
    {
        // Arrange — a goal_status attachment with met:false carries an active /goal condition.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-goal";
        var projectDir = Path.Combine(tempDir, "projects", "p");
        Directory.CreateDirectory(projectDir);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, $"{sessionId}.jsonl"),
            string.Join(
                '\n',
                JsonSerializer.Serialize(new { type = "user", cwd = "/repo", slug = "s", message = new { role = "user", content = "Hi" } }),
                JsonSerializer.Serialize(new { type = "attachment", attachment = new { type = "goal_status", met = false, condition = "port all relevant upstream functionality" } })),
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle();
        sessions[0].Goal.Should().Be("port all relevant upstream functionality");
    }

    [Fact]
    public async Task GetSessions_TreatsMetGoalAsCleared()
    {
        // Arrange — a goal set, then later met (auto-cleared by Claude Code). Last-write-wins → no active goal.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-goal-met";
        var projectDir = Path.Combine(tempDir, "projects", "p");
        Directory.CreateDirectory(projectDir);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, $"{sessionId}.jsonl"),
            string.Join(
                '\n',
                JsonSerializer.Serialize(new { type = "user", cwd = "/repo", slug = "s", message = new { role = "user", content = "Hi" } }),
                JsonSerializer.Serialize(new { type = "attachment", attachment = new { type = "goal_status", met = false, condition = "do the thing" } }),
                JsonSerializer.Serialize(new { type = "attachment", attachment = new { type = "goal_status", met = true, condition = "do the thing" } })),
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle();
        sessions[0].Goal.Should().BeNull();
    }

    [Fact]
    public async Task GetSessions_ExtractsGoalSetBeyondHeadWindowFromTail()
    {
        // Arrange — a /goal set deep in a long session (past the 250-line head window and far
        // enough in that only the tail scan reaches it). The tail value must still be surfaced.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-goal-tail";
        var projectDir = Path.Combine(tempDir, "projects", "p");
        Directory.CreateDirectory(projectDir);

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 400; i++)
        {
            builder.AppendLine(JsonSerializer.Serialize(new { type = "user", cwd = "/repo", slug = "s", message = new { role = "user", content = $"filler line {i} with padding to grow the file well beyond the tail-read window" } }));
        }

        builder.Append(JsonSerializer.Serialize(new { type = "attachment", attachment = new { type = "goal_status", met = false, condition = "late goal from tail" } }));

        await File.WriteAllTextAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), builder.ToString(), cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle();
        sessions[0].Goal.Should().Be("late goal from tail");
    }

    [Fact]
    public async Task GetSessions_PreservesProgressAfterTaskFilesRemoved()
    {
        // Arrange — create a session with 3 tasks, then remove the task files
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-snap");

        Directory.CreateDirectory(sessionDir);

        var taskFile1 = Path.Combine(sessionDir, "1.json");
        var taskFile2 = Path.Combine(sessionDir, "2.json");
        var taskFile3 = Path.Combine(sessionDir, "3.json");

        await File.WriteAllTextAsync(
            taskFile1,
            JsonSerializer.Serialize(new { id = "1", subject = "Task 1", status = "completed" }),
            cancellationToken);

        await File.WriteAllTextAsync(
            taskFile2,
            JsonSerializer.Serialize(new { id = "2", subject = "Task 2", status = "completed" }),
            cancellationToken);

        await File.WriteAllTextAsync(
            taskFile3,
            JsonSerializer.Serialize(new { id = "3", subject = "Task 3", status = "pending" }),
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // First call snapshots the session with 3 tasks
        var before = await service.GetSessionsAsync(cancellationToken: cancellationToken);
        before.Should().HaveCount(1);
        before[0].TaskCount.Should().Be(3);

        // Expire the memory cache so the next call re-discovers from disk
        cache.Remove("sessions:20");

        // Remove all task files (directory stays)
        File.Delete(taskFile1);
        File.Delete(taskFile2);
        File.Delete(taskFile3);

        // Act — re-fetch sessions; directory exists but is empty
        var after = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert — progress should be restored from snapshot, marked as completed
        after.Should().HaveCount(1);

        var session = after[0];
        session.IsCompleted.Should().BeTrue();
        session.Progress.Should().Be(100);
        session.TaskCount.Should().Be(3);
        session.Completed.Should().Be(3);
        session.Pending.Should().Be(0);
        session.InProgress.Should().Be(0);
    }

    [Fact]
    public async Task GetSessions_UsesAiTitleWhenNoCustomTitle()
    {
        // Arrange — a background-agent session emits ai-title/agent-name but no custom-title.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-bg";
        var projectDir = Path.Combine(tempDir, "projects", "proj");
        Directory.CreateDirectory(projectDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", cwd = "/repo", message = new { role = "user", content = "Hi" } }),
            JsonSerializer.Serialize(new { type = "agent-name", agentName = "explorer-bot" }),
            JsonSerializer.Serialize(new { type = "ai-title", aiTitle = "Investigate flaky test" }));
        await File.WriteAllTextAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), jsonl, cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert — ai-title wins over agent-name when no custom-title is present.
        sessions.Should().HaveCount(1);
        sessions[0].Name.Should().Be("Investigate flaky test");
    }

    [Fact]
    public async Task GetSessions_PrefersCustomTitleOverAiTitle()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-renamed";
        var projectDir = Path.Combine(tempDir, "projects", "proj");
        Directory.CreateDirectory(projectDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", cwd = "/repo", message = new { role = "user", content = "Hi" } }),
            JsonSerializer.Serialize(new { type = "ai-title", aiTitle = "Auto title" }),
            JsonSerializer.Serialize(new { type = "custom-title", customTitle = "My Rename" }));
        await File.WriteAllTextAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), jsonl, cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert — the user's custom-title takes priority over the ai-title.
        sessions.Should().HaveCount(1);
        sessions[0].Name.Should().Be("My Rename");
    }

    [Fact]
    public async Task GetSessions_MarksAutoSelfTeamAsPlainSession()
    {
        // Arrange — recent Claude Code writes a single-member self-team (sole team-lead)
        // for every session; it must not surface as a team.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-selfteam";

        await WriteTaskAsync(sessionId, "1", "pending", cancellationToken);

        await WriteTeamConfigAsync(
            sessionId,
            new
            {
                name = sessionId,
                leadSessionId = sessionId,
                members = new[] { new { agentId = "team-lead@" + sessionId, name = "team-lead", agentType = "team-lead" } },
            },
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().HaveCount(1);
        sessions[0].IsTeam.Should().BeFalse();
        sessions[0].MemberCount.Should().Be(0);
    }

    [Fact]
    public async Task GetSessions_MarksMultiMemberTeamAsTeam()
    {
        // Arrange — a genuinely-named multi-member team must still be a team.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string teamName = "research-team";

        await WriteTaskAsync(teamName, "1", "pending", cancellationToken);

        await WriteTeamConfigAsync(
            teamName,
            new
            {
                name = teamName,
                members = new[]
                {
                    new { agentId = "team-lead@" + teamName, name = "team-lead", agentType = "team-lead" },
                    new { agentId = "researcher@" + teamName, name = "researcher", agentType = "researcher" },
                },
            },
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().HaveCount(1);
        sessions[0].IsTeam.Should().BeTrue();
        sessions[0].MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSessions_TreatsSelfTeamWithRealTeammateAsTeam()
    {
        // Arrange — a session-named team that gained a real teammate is a team again.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-grew";

        await WriteTaskAsync(sessionId, "1", "pending", cancellationToken);

        await WriteTeamConfigAsync(
            sessionId,
            new
            {
                name = sessionId,
                leadSessionId = sessionId,
                members = new[]
                {
                    new { agentId = "team-lead@" + sessionId, name = "team-lead", agentType = "team-lead" },
                    new { agentId = "helper@" + sessionId, name = "helper", agentType = "general-purpose" },
                },
            },
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().HaveCount(1);
        sessions[0].IsTeam.Should().BeTrue();
        sessions[0].MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSessions_MergesSelfTeamTasksIntoOwnerByLeadSessionId()
    {
        // Arrange — a self-team whose leadSessionId is a discovered UUID session.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ownerId = "11111111-1111-4111-8111-111111111111";

        await WriteSessionJsonlAsync(ownerId, "Owner Work", cancellationToken);
        await WriteTaskAsync("session-11111111", "1", "pending", cancellationToken);
        await WriteTaskAsync("session-11111111", "2", "completed", cancellationToken);
        await WriteTeamConfigAsync(
            "session-11111111",
            new
            {
                name = "session-11111111",
                leadSessionId = ownerId,
                members = new[] { new { agentId = "team-lead@x", name = "team-lead", agentType = "team-lead" } },
            },
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert — one merged card, tasks attached, no session-<prefix> duplicate.
        sessions.Should().NotContain(s => s.Id == "session-11111111");
        var owner = sessions.Should().ContainSingle(s => s.Id == ownerId).Subject;
        owner.TaskCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSessions_MergesSelfTeamTasksIntoOwnerByPrefix_WhenNoTeamConfig()
    {
        // Arrange — a session-<prefix> task dir with no team config; prefix matches the UUID.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ownerId = "22222222-2222-4222-8222-222222222222";

        await WriteSessionJsonlAsync(ownerId, "Prefix Owner", cancellationToken);
        await WriteTaskAsync("session-22222222", "1", "pending", cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().NotContain(s => s.Id == "session-22222222");
        var owner = sessions.Should().ContainSingle(s => s.Id == ownerId).Subject;
        owner.TaskCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSessions_MergesResumedSelfTeamTasksIntoOwnerViaRegistry()
    {
        // Arrange — a resumed session: the team's leadSessionId is a ghost and the dir prefix
        // matches no discovered session, so only the live-session registry (cwd + boot time)
        // links the self-team to the live owner.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ownerId = "33333333-3333-4333-8333-333333333333";
        const string cwd = "/repo/live";

        await WriteSessionJsonlAsync(ownerId, "Resumed Owner", cancellationToken);
        await WriteLiveSessionAsync("4242", ownerId, cwd, startedAt: 1_000_000, cancellationToken);
        await WriteTaskAsync("session-aaaaaaaa", "1", "pending", cancellationToken);
        await WriteTaskAsync("session-aaaaaaaa", "2", "in_progress", cancellationToken);
        await WriteTaskAsync("session-aaaaaaaa", "3", "completed", cancellationToken);
        await WriteTeamConfigAsync(
            "session-aaaaaaaa",
            new
            {
                name = "session-aaaaaaaa",
                leadSessionId = "99999999-9999-4999-8999-999999999999",
                createdAt = 1_000_500L,
                members = new[] { new { agentId = "team-lead@x", name = "team-lead", agentType = "team-lead", cwd } },
            },
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().NotContain(s => s.Id == "session-aaaaaaaa");
        var owner = sessions.Should().ContainSingle(s => s.Id == ownerId).Subject;
        owner.TaskCount.Should().Be(3);
    }

    [Fact]
    public async Task GetSessions_KeepsSelfTeamCard_WhenNoOwnerResolves()
    {
        // Arrange — a self-team whose owner is gone (ghost lead, no matching registry session,
        // no discovered prefix UUID). The card must stay so its tasks remain visible.
        var cancellationToken = TestContext.Current.CancellationToken;

        await WriteTaskAsync("session-bbbbbbbb", "1", "pending", cancellationToken);
        await WriteTeamConfigAsync(
            "session-bbbbbbbb",
            new
            {
                name = "session-bbbbbbbb",
                leadSessionId = "88888888-8888-4888-8888-888888888888",
                createdAt = 2_000_000L,
                members = new[] { new { agentId = "team-lead@x", name = "team-lead", agentType = "team-lead", cwd = "/repo/orphan" } },
            },
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        var card = sessions.Should().ContainSingle(s => s.Id == "session-bbbbbbbb").Subject;
        card.TaskCount.Should().Be(1);
    }

    private Task WriteSessionJsonlAsync(
        string sessionId,
        string aiTitle,
        CancellationToken cancellationToken)
    {
        var projectDir = Path.Combine(tempDir, "projects", "proj");
        Directory.CreateDirectory(projectDir);
        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", cwd = "/repo", message = new { role = "user", content = "Hi" } }),
            JsonSerializer.Serialize(new { type = "ai-title", aiTitle }));

        return File.WriteAllTextAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), jsonl, cancellationToken);
    }

    private Task WriteLiveSessionAsync(
        string pid,
        string sessionId,
        string cwd,
        long startedAt,
        CancellationToken cancellationToken)
    {
        var sessionsDir = Path.Combine(tempDir, "sessions");
        Directory.CreateDirectory(sessionsDir);

        // kind "bg" (a background session) deliberately — the owner is matched on cwd + boot
        // time regardless of kind; a stricter interactive-only filter would wrongly skip it.
        return File.WriteAllTextAsync(
            Path.Combine(sessionsDir, $"{pid}.json"),
            JsonSerializer.Serialize(new { sessionId, kind = "bg", cwd, startedAt, status = "busy" }),
            cancellationToken);
    }

    private Task WriteTaskAsync(
        string sessionId,
        string taskId,
        string status,
        CancellationToken cancellationToken)
    {
        var sessionDir = Path.Combine(tempDir, "tasks", sessionId);
        Directory.CreateDirectory(sessionDir);

        return File.WriteAllTextAsync(
            Path.Combine(sessionDir, $"{taskId}.json"),
            JsonSerializer.Serialize(new { id = taskId, subject = "Task " + taskId, status }),
            cancellationToken);
    }

    private Task WriteTeamConfigAsync(
        string teamName,
        object config,
        CancellationToken cancellationToken)
    {
        var teamDir = Path.Combine(tempDir, "teams", teamName);
        Directory.CreateDirectory(teamDir);

        return File.WriteAllTextAsync(
            Path.Combine(teamDir, "config.json"),
            JsonSerializer.Serialize(config),
            cancellationToken);
    }
}