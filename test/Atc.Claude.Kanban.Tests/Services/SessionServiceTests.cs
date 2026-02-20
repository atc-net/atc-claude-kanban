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
        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService);

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

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService);

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

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService);

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

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService);

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
        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService);

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

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService);

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

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService);

        // Act
        var sessions = await service.GetSessionsAsync(cancellationToken: cancellationToken);

        // Assert — session directory exists so session should appear with taskCount=0
        sessions.Should().HaveCount(1);
        sessions[0].Id.Should().Be("session-empty");
        sessions[0].TaskCount.Should().Be(0);
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

        var service = new SessionService(tempDir, cache, jsonSerializerOptions, subagentService);

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
}