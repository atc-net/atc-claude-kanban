namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="TaskService"/>.
/// </summary>
public sealed class TaskServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache memoryCache;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly SessionService sessionService;

    public TaskServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        memoryCache = new MemoryCache(new MemoryCacheOptions());
        jsonSerializerOptions = JsonSerializerOptionsFactory.Create();
        var subagentService = new SubagentService(tempDir, memoryCache);
        sessionService = new SessionService(tempDir, memoryCache, jsonSerializerOptions, subagentService);
    }

    public void Dispose()
    {
        memoryCache.Dispose();

        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetTasksForSession_ReturnsEmpty_WhenSessionNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var tasks = await service.GetTasksForSessionAsync("nonexistent", cancellationToken);

        // Assert
        tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTasksForSession_ReturnsTasks()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-1");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "First task", status = "pending" }),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "2.json"),
            JsonSerializer.Serialize(new { id = "2", subject = "Second task", status = "completed" }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var tasks = await service.GetTasksForSessionAsync("session-1", cancellationToken);

        // Assert
        tasks.Should().HaveCount(2);
        tasks.Should().Contain(t => t.Id == "1" && t.Subject == "First task");
        tasks.Should().Contain(t => t.Id == "2" && t.Subject == "Second task");
    }

    [Fact]
    public async Task AddNote_AppendsToDescription()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-note");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Test", status = "pending", description = "Original text" }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var result = await service.AddNoteAsync("session-note", "1", "My note", cancellationToken);

        // Assert
        result.Should().NotBeNull();
        var tasks = await service.GetTasksForSessionAsync("session-note", cancellationToken);
        tasks[0].Description.Should().Contain("Original text");
        tasks[0].Description.Should().Contain("#### [Note added by user]");
        tasks[0].Description.Should().Contain("My note");
    }

    [Fact]
    public async Task DeleteTask_SucceedsForUnblockedTask()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-del");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Delete me", status = "pending" }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var (success, error, _) = await service.DeleteTaskAsync("session-del", "1", cancellationToken);

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();
        File.Exists(Path.Combine(sessionDir, "1.json")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteTask_FailsWhenOtherTasksDependOnIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-dep");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Blocker", status = "pending" }),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "2.json"),
            JsonSerializer.Serialize(new { id = "2", subject = "Blocked", status = "pending", blockedBy = new[] { "1" } }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var (success, error, blockedTasks) = await service.DeleteTaskAsync("session-dep", "1", cancellationToken);

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("blocks other tasks");
        blockedTasks.Should().Contain("2");
    }

    [Fact]
    public async Task GetAllTasks_AggregatesAcrossSessions()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var session1Dir = Path.Combine(tempDir, "tasks", "session-a");
        var session2Dir = Path.Combine(tempDir, "tasks", "session-b");
        Directory.CreateDirectory(session1Dir);
        Directory.CreateDirectory(session2Dir);

        await File.WriteAllTextAsync(
            Path.Combine(session1Dir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Task A1", status = "pending" }),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(session2Dir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Task B1", status = "completed" }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var allTasks = await service.GetAllTasksAsync(cancellationToken);

        // Assert
        allTasks.Should().HaveCount(2);
        allTasks.Should().Contain(t => t.SessionId == "session-a");
        allTasks.Should().Contain(t => t.SessionId == "session-b");
    }

    [Fact]
    public async Task UpdateTask_MergesFieldsIntoFile()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-upd");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Old title", status = "pending" }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        var updates = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["subject"] = JsonSerializer.SerializeToElement("New title"),
        };

        // Act
        var result = await service.UpdateTaskAsync("session-upd", "1", updates, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        var tasks = await service.GetTasksForSessionAsync("session-upd", cancellationToken);
        tasks[0].Subject.Should().Be("New title");
    }

    [Fact]
    public async Task GetAgentTasksForSession_ReturnsOnlyInternalTasks()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-agents");
        Directory.CreateDirectory(sessionDir);

        // Regular task (no _internal metadata)
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Regular task", status = "pending" }),
            cancellationToken);

        // Internal agent task (with _internal = true in metadata)
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "2.json"),
            JsonSerializer.Serialize(new { id = "2", subject = "Agent lifecycle", status = "completed", metadata = new { _internal = true } }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var agentTasks = await service.GetAgentTasksForSessionAsync("session-agents", cancellationToken);

        // Assert
        agentTasks.Should().HaveCount(1);
        agentTasks[0].Id.Should().Be("2");
        agentTasks[0].Subject.Should().Be("Agent lifecycle");
    }

    [Fact]
    public async Task GetAgentTasksForSession_ReturnsEmpty_WhenNoInternalTasks()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-no-agents");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Regular task", status = "pending" }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var agentTasks = await service.GetAgentTasksForSessionAsync("session-no-agents", cancellationToken);

        // Assert
        agentTasks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAgentTasksForSession_ReturnsEmpty_WhenSessionNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var agentTasks = await service.GetAgentTasksForSessionAsync("nonexistent", cancellationToken);

        // Assert
        agentTasks.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateTask_RejectsDisallowedFields()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionDir = Path.Combine(tempDir, "tasks", "session-filter");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "1.json"),
            JsonSerializer.Serialize(new { id = "1", subject = "Original", status = "pending" }),
            cancellationToken);

        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);
        var updates = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["id"] = JsonSerializer.SerializeToElement("hacked-id"),
            ["subject"] = JsonSerializer.SerializeToElement("Updated title"),
        };

        // Act
        await service.UpdateTaskAsync("session-filter", "1", updates, cancellationToken);

        // Assert — "id" should not have been overwritten
        var tasks = await service.GetTasksForSessionAsync("session-filter", cancellationToken);
        tasks[0].Id.Should().Be("1");
        tasks[0].Subject.Should().Be("Updated title");
    }

    [Fact]
    public async Task DeleteTask_ReturnsError_WhenTaskNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new TaskService(tempDir, sessionService, jsonSerializerOptions);

        // Act
        var (success, error, _) = await service.DeleteTaskAsync("nonexistent", "999", cancellationToken);

        // Assert
        success.Should().BeFalse();
        error.Should().Be("Task not found");
    }
}