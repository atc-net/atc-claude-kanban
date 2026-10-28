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
    public async Task GetSessions_ShowsLatestGitBranch_WhenSessionSwitchesBranch()
    {
        // Arrange - a short session that starts on a feature branch and moves to main
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-branch-switch";
        var projectDir = Path.Combine(tempDir, "projects", "p");
        Directory.CreateDirectory(projectDir);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, $"{sessionId}.jsonl"),
            string.Join(
                '\n',
                JsonSerializer.Serialize(new { type = "user", cwd = "/repo", gitBranch = "feature/old", message = new { role = "user", content = "Hi" } }),
                JsonSerializer.Serialize(new { type = "user", cwd = "/repo", gitBranch = "main", message = new { role = "user", content = "Merged" } })),
            cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle().Which.GitBranch.Should().Be("main");
    }

    [Fact]
    public async Task GetSessions_ShowsLatestGitBranch_WhenSwitchIsBeyondHeadWindow()
    {
        // Arrange - a long session whose branch switch only the tail scan can reach, with a
        // sessions-index.json entry still recording the branch it started on
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "session-branch-tail";
        var projectDir = Path.Combine(tempDir, "projects", "p");
        Directory.CreateDirectory(projectDir);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "sessions-index.json"),
            JsonSerializer.Serialize(new { originalPath = "/repo", entries = new[] { new { sessionId, projectPath = "/repo", gitBranch = "feature/old" } } }),
            cancellationToken);

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 400; i++)
        {
            builder.AppendLine(JsonSerializer.Serialize(new { type = "user", cwd = "/repo", gitBranch = "feature/old", message = new { role = "user", content = $"filler line {i} with padding to grow the file well beyond the tail-read window" } }));
        }

        builder.AppendLine(JsonSerializer.Serialize(new { type = "user", cwd = "/repo", gitBranch = "main", message = new { role = "user", content = "Merged" } }));
        builder.Append(JsonSerializer.Serialize(new { type = "last-prompt", lastPrompt = "done" }));

        await File.WriteAllTextAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), builder.ToString(), cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle().Which.GitBranch.Should().Be("main");
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

    [Theory]
    [InlineData(3, 1)]
    [InlineData(1, 3)]
    public async Task GetSessions_PrefersResumedSelfTeamWithTeammatesOverStalePrefixList(
        int liveTaskCount,
        int staleTaskCount)
    {
        // Arrange — a resumed session: its original list (session-{own prefix}) is stale, and the
        // list the running process writes to sits under a ghost-led self-team that teammates
        // have since joined. Only the registry links it back, and it wins regardless of size.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ownerId = "44444444-4444-4444-8444-444444444444";
        const string cwd = "/repo/resumed";

        await WriteSessionJsonlAsync(ownerId, "Resumed Owner", cancellationToken);
        await WriteLiveSessionAsync("4343", ownerId, cwd, startedAt: 3_000_000, cancellationToken);
        for (var i = 1; i <= staleTaskCount; i++)
        {
            await WriteTaskAsync("session-44444444", i.ToString(CultureInfo.InvariantCulture), "completed", cancellationToken);
        }

        for (var i = 1; i <= liveTaskCount; i++)
        {
            await WriteTaskAsync("session-cccccccc", i.ToString(CultureInfo.InvariantCulture), "pending", cancellationToken);
        }

        await WriteTeamConfigAsync(
            "session-cccccccc",
            new
            {
                name = "session-cccccccc",
                leadSessionId = "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
                createdAt = 3_000_400L,
                members = new[]
                {
                    new { agentId = "team-lead@session-cccccccc", name = "team-lead", agentType = "team-lead", cwd },
                    new { agentId = "helper@session-cccccccc", name = "helper", agentType = "general-purpose", cwd },
                },
            },
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);
        var taskDir = await service.GetCustomTaskDirAsync(ownerId, cancellationToken);

        // Assert
        sessions.Should().NotContain(s => s.Id == "session-cccccccc" || s.Id == "session-44444444");
        var owner = sessions.Should().ContainSingle(s => s.Id == ownerId).Subject;
        owner.TaskCount.Should().Be(liveTaskCount);
        owner.Pending.Should().Be(liveTaskCount);
        owner.Completed.Should().Be(0);
        taskDir.Should().Be(Path.GetFullPath(Path.Combine(tempDir, "tasks", "session-cccccccc")));
    }

    [Fact]
    public async Task GetSessions_HidesEmptyOrphanTaskList()
    {
        // Arrange — a session-{prefix} list left empty with nothing to tie it to a session: no
        // task files, no team config, and no transcript whose id carries the prefix. A
        // session-named list that still holds tasks keeps its card.
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.Combine(tempDir, "tasks", "session-eeeeeeee"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, "tasks", "session-eeeeeeee", ".highwatermark"), "19", cancellationToken);
        await WriteTaskAsync("session-ffffffff", "1", "pending", cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().NotContain(s => s.Id == "session-eeeeeeee");
        sessions.Should().ContainSingle(s => s.Id == "session-ffffffff");
    }

    [Theory]
    [InlineData(11, true, true)]
    [InlineData(9, true, false)]
    [InlineData(11, false, false)]
    public async Task GetSessions_DropsPrefixList_WhenTranscriptCreatedTasksElsewhereAfterIt(
        int createHourUtc,
        bool createSucceeded,
        bool expectSuperseded)
    {
        // Arrange — an ended session (no registry entry, no team config left) whose own
        // session-{prefix} list last changed at 10:00. A successful TaskCreate in its transcript
        // after that went to another list (it was resumed), so the prefix list is stale.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ownerId = "66666666-6666-4666-8666-666666666666";
        var listChangedUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var createUtc = new DateTime(2026, 1, 1, createHourUtc, 0, 0, DateTimeKind.Utc);

        await WriteTaskAsync("session-66666666", "1", "pending", cancellationToken);
        await WriteTaskAsync("session-66666666", "2", "in_progress", cancellationToken);
        var prefixDir = Path.Combine(tempDir, "tasks", "session-66666666");
        Directory.SetLastWriteTimeUtc(prefixDir, listChangedUtc);
        Directory.CreateDirectory(Path.Combine(tempDir, "teams"));
        await WriteTaskCreateTranscriptAsync(ownerId, createUtc, createSucceeded, cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);
        var taskDir = await service.GetCustomTaskDirAsync(ownerId, cancellationToken);

        // Assert
        sessions.Should().NotContain(s => s.Id == "session-66666666");
        var owner = sessions.Should().ContainSingle(s => s.Id == ownerId).Subject;
        if (expectSuperseded)
        {
            owner.TaskCount.Should().Be(0);
            taskDir.Should().BeNull();
        }
        else
        {
            owner.TaskCount.Should().Be(2);
            taskDir.Should().Be(Path.GetFullPath(prefixDir));
        }
    }

    [Fact]
    public async Task GetSessions_DoesNotMergeNamedTeamIntoSessionBootedAlongsideIt()
    {
        // Arrange — a user-named team created in the same cwd within the boot window of a live
        // session. Only Claude Code's own session-{prefix} teams may be bridged to a session.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string sessionId = "55555555-5555-4555-8555-555555555555";
        const string cwd = "/repo/named";

        await WriteSessionJsonlAsync(sessionId, "Neighbour", cancellationToken);
        await WriteLiveSessionAsync("4444", sessionId, cwd, startedAt: 4_000_000, cancellationToken);
        await WriteTaskAsync("review-squad", "1", "pending", cancellationToken);
        await WriteTeamConfigAsync(
            "review-squad",
            new
            {
                name = "review-squad",
                leadSessionId = "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
                createdAt = 4_000_300L,
                members = new[]
                {
                    new { agentId = "team-lead@review-squad", name = "team-lead", agentType = "team-lead", cwd },
                    new { agentId = "reviewer@review-squad", name = "reviewer", agentType = "general-purpose", cwd },
                },
            },
            cancellationToken);

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);
        var taskDir = await service.GetCustomTaskDirAsync(sessionId, cancellationToken);

        // Assert
        sessions.Should().ContainSingle(s => s.Id == "review-squad");
        sessions.Should().ContainSingle(s => s.Id == sessionId).Subject.TaskCount.Should().Be(0);
        taskDir.Should().BeNull();
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

    private Task WriteTaskCreateTranscriptAsync(
        string sessionId,
        DateTime createUtc,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var projectDir = Path.Combine(tempDir, "projects", "proj");
        Directory.CreateDirectory(projectDir);

        var toolUse = new
        {
            type = "assistant",
            timestamp = createUtc.ToString("O", CultureInfo.InvariantCulture),
            message = new
            {
                role = "assistant",
                content = new object[] { new { type = "tool_use", id = "toolu_create", name = "TaskCreate", input = new { subject = "Next" } } },
            },
        };
        object toolResult = succeeded
            ? new
            {
                type = "user",
                timestamp = createUtc.AddSeconds(1).ToString("O", CultureInfo.InvariantCulture),
                message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = "toolu_create", content = "Task #3 created successfully: Next" } } },
                toolUseResult = new { task = new { id = "3", subject = "Next" } },
            }
            : new
            {
                type = "user",
                timestamp = createUtc.AddSeconds(1).ToString("O", CultureInfo.InvariantCulture),
                message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = "toolu_create", content = "Error: invalid input", is_error = true } } },
                toolUseResult = "Error: invalid input",
            };
        var reply = new
        {
            type = "assistant",
            timestamp = createUtc.AddMinutes(5).ToString("O", CultureInfo.InvariantCulture),
            message = new { role = "assistant", content = new object[] { new { type = "text", text = "Done." } } },
        };

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", cwd = "/repo", timestamp = "2026-01-01T08:00:00.000Z", message = new { role = "user", content = "Hi" } }),
            JsonSerializer.Serialize(toolUse),
            JsonSerializer.Serialize(toolResult),
            JsonSerializer.Serialize(reply)) + "\n";

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

    private Task WriteRegistryEntryAsync(
        int pid,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var sessionsDir = Path.Combine(tempDir, "sessions");
        Directory.CreateDirectory(sessionsDir);

        return File.WriteAllTextAsync(
            Path.Combine(sessionsDir, $"{pid}.json"),
            JsonSerializer.Serialize(new { pid, sessionId, kind = "interactive", cwd = "/repo", startedAt = 0L, status = "busy" }),
            cancellationToken);
    }

    [Fact]
    public async Task GetSessions_MarksSessionRunning_WhenRegistryProcessIsAlive()
    {
        // Arrange - the registry entry points at this test process, which is alive
        var cancellationToken = TestContext.Current.CancellationToken;
        await WriteTaskAsync("session-live", "1", "in_progress", cancellationToken);
        await WriteRegistryEntryAsync(Environment.ProcessId, "session-live", cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle(s => s.Id == "session-live").Subject.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task GetSessions_LeavesSessionNotRunning_WhenRegistryProcessHasExited()
    {
        // Arrange - a crash leaves the registry file behind with a pid no process has
        var cancellationToken = TestContext.Current.CancellationToken;
        await WriteTaskAsync("session-dead", "1", "in_progress", cancellationToken);
        await WriteRegistryEntryAsync(int.MaxValue, "session-dead", cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        var session = sessions.Should().ContainSingle(s => s.Id == "session-dead").Subject;
        session.IsRunning.Should().BeFalse();
        session.InProgress.Should().Be(1);
    }

    [Fact]
    public async Task GetSessions_LeavesSessionNotRunning_WhenAbsentFromRegistry()
    {
        // Arrange - a stopped session keeps its unfinished task but has no registry entry
        var cancellationToken = TestContext.Current.CancellationToken;
        await WriteTaskAsync("session-gone", "1", "in_progress", cancellationToken);
        await WriteRegistryEntryAsync(Environment.ProcessId, "some-other-session", cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle(s => s.Id == "session-gone").Subject.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task GetSessions_MarksTeamRunning_WhenLeadSessionIsInRegistry()
    {
        // Arrange - a team card is keyed by team name; its lead session is the running process
        var cancellationToken = TestContext.Current.CancellationToken;
        const string leadId = "aaaaaaaa-1111-2222-3333-444444444444";
        await WriteTaskAsync("review-squad", "1", "in_progress", cancellationToken);
        await WriteTeamConfigAsync(
            "review-squad",
            new
            {
                name = "review-squad",
                leadSessionId = leadId,
                members = new[]
                {
                    new { agentId = "team-lead@review-squad", name = "team-lead", agentType = "team-lead", cwd = "/repo" },
                    new { agentId = "reviewer@review-squad", name = "reviewer", agentType = "general-purpose", cwd = "/repo" },
                },
            },
            cancellationToken);
        await WriteRegistryEntryAsync(Environment.ProcessId, leadId, cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle(s => s.Id == "review-squad").Subject.IsRunning.Should().BeTrue();
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

    [Fact]
    public async Task GetSessions_PrefersTranscriptTime_WhenTranscriptIsNewerThanTaskDirectory()
    {
        // Arrange - the conversation is live while the task files sit untouched
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-abc");
        Directory.CreateDirectory(sessionDir);

        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Test task", status = "pending" }),
            cancellationToken);

        File.SetLastWriteTimeUtc(Path.Combine(sessionDir, "1.json"), StaleUtc);
        Directory.SetLastWriteTimeUtc(sessionDir, StaleUtc);
        var transcriptUtc = await WriteTranscriptAsync("session-abc", cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle();
        sessions[0].ModifiedAt.Should().BeCloseTo(transcriptUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetSessions_PrefersTranscriptTime_ForSubagentOnlySession()
    {
        // Arrange - subagents finished long ago but the lead conversation continues
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "D--test-project", "session-xyz");
        var subagentsDir = Path.Combine(projectDir, "subagents");
        Directory.CreateDirectory(subagentsDir);

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-1.jsonl"),
            JsonSerializer.Serialize(new { type = "assistant" }),
            cancellationToken);

        Directory.SetLastWriteTimeUtc(projectDir, StaleUtc);
        var transcriptUtc = await WriteTranscriptAsync("session-xyz", cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle();
        sessions[0].ModifiedAt.Should().BeCloseTo(transcriptUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetSessions_KeepsTaskDirectoryTime_WhenTranscriptIsOlder()
    {
        // Arrange - a resumed transcript must never drag a fresh task list backwards
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-abc");
        Directory.CreateDirectory(sessionDir);

        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Test task", status = "pending" }),
            cancellationToken);

        await WriteTranscriptAsync("session-abc", cancellationToken);
        SetTranscriptWriteTimeUtc("session-abc", StaleUtc);

        var taskFileUtc = File.GetLastWriteTimeUtc(Path.Combine(sessionDir, "1.json"));
        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        sessions.Should().ContainSingle();
        sessions[0].ModifiedAt.Should().BeCloseTo(taskFileUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetSessions_TakesTranscriptTimeFromTheLead_ForATeamRow()
    {
        // Arrange - a team row is keyed by team name and has no transcript of its own, so its
        // recency has to come from the lead session that is actually talking.
        var cancellationToken = TestContext.Current.CancellationToken;
        const string teamName = "research-team";
        const string leadId = "lead-session";

        await WriteTaskAsync(teamName, "1", "pending", cancellationToken);

        await WriteTeamConfigAsync(
            teamName,
            new
            {
                name = teamName,
                leadSessionId = leadId,
                members = new[]
                {
                    new { agentId = "team-lead@" + teamName, name = "team-lead", agentType = "team-lead" },
                    new { agentId = "researcher@" + teamName, name = "researcher", agentType = "researcher" },
                },
            },
            cancellationToken);

        var teamDir = Path.Combine(tempDir, "tasks", teamName);
        File.SetLastWriteTimeUtc(Path.Combine(teamDir, "1.json"), StaleUtc);
        Directory.SetLastWriteTimeUtc(teamDir, StaleUtc);

        var transcriptUtc = await WriteTranscriptAsync(leadId, cancellationToken);

        var service = CreateService();

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert
        var team = sessions.Single(s => s.Id == teamName);
        team.IsTeam.Should().BeTrue();
        team.ModifiedAt.Should().BeCloseTo(transcriptUtc, TimeSpan.FromSeconds(1));
    }

    private static readonly DateTime StaleUtc = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private SessionService CreateService()
        => new(tempDir, cache, jsonSerializerOptions, subagentService, new SessionActivityService(tempDir, cache));

    private string TranscriptPath(string sessionId)
        => Path.Combine(tempDir, "projects", "D--test-project", sessionId + ".jsonl");

    private void SetTranscriptWriteTimeUtc(
        string sessionId,
        DateTime writeTimeUtc)
        => File.SetLastWriteTimeUtc(TranscriptPath(sessionId), writeTimeUtc);

    private async Task<DateTime> WriteTranscriptAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var transcript = TranscriptPath(sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(transcript)!);

        await File.WriteAllTextAsync(
            transcript,
            JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "text", text = "hi" } } } }),
            cancellationToken);

        return File.GetLastWriteTimeUtc(transcript);
    }
}